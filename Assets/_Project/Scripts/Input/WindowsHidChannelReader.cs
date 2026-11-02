#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace GateRush.Input
{
    /// <summary>
    /// Reads a USB joystick/radio straight from Windows (hid.dll), bypassing the Input System. A background thread
    /// reads every HID input report and decodes it with the device's own report descriptor (HidP_*), so it works
    /// whatever window has focus and whatever the Input System makes of the device.
    /// Channel names and order copy <see cref="InputSystemChannelReader"/> (axes x, y, z, rx, ry, rz, slider...,
    /// then buttons sorted by name with button 1 called "trigger"; y and ry flipped like Unity's HID layouts),
    /// so presets and saved calibrations work with either backend.
    /// </summary>
    public sealed unsafe class WindowsHidChannelReader : IChannelReader, IDisposable
    {
        private const ushort GenericDesktopPage = 0x01;
        private const ushort ButtonPage = 0x09;
        private const int HidpInput = 0;
        private const int HidpStatusSuccess = 0x00110000;
        private const int CapsSize = 72;
        private const int ErrorIoPending = 997;
        private const uint WaitTimeout = 0x102;

        private static readonly string[] AxisOrder = { "x", "y", "z", "rx", "ry", "rz", "throttle", "rudder", "slider", "dial", "wheel" };

        private struct Channel
        {
            public string Name;
            public ushort Page;
            public ushort Usage;
            public ushort Link;
            public bool Button;
            public bool Invert;
            public int Min;
            public int Max;
            public int Bits;
        }

        private readonly IntPtr _handle;
        private readonly IntPtr _preparsed;
        private readonly int _reportLength;
        private readonly Channel[] _channels;
        private readonly float[] _values;
        private readonly ushort[] _usageBuffer = new ushort[128];
        private readonly Thread _thread;
        private volatile bool _stop;
        private volatile bool _connected = true;
        private int _reports;

        private WindowsHidChannelReader(string path, IntPtr handle, IntPtr preparsed, int reportLength, string product, int vid, int pid)
        {
            Path = path;
            _handle = handle;
            _preparsed = preparsed;
            _reportLength = reportLength;
            ProductName = product;
            DeviceKey = $"{product}|{vid:X4}|{pid:X4}";

            var axes = new List<Channel>();
            var buttons = new List<Channel>();
            ReadValueChannels(axes);
            ReadButtonChannels(buttons);
            axes.Sort((p, q) => Rank(p.Name) != Rank(q.Name) ? Rank(p.Name).CompareTo(Rank(q.Name)) : string.CompareOrdinal(p.Name, q.Name));
            buttons.Sort((p, q) => string.CompareOrdinal(p.Name, q.Name));
            AxisCount = axes.Count;
            axes.AddRange(buttons);
            _channels = axes.ToArray();
            _values = new float[_channels.Length];

            _thread = new Thread(Run) { IsBackground = true, Name = "GateRush HID " + product };
            _thread.Start();
        }

        public string Path { get; }
        public int AxisCount { get; }
        /// <summary>Input reports received so far (diagnostics).</summary>
        public int ReportCount => _reports;
        public string DeviceKey { get; }
        public string ProductName { get; }
        public string DisplayName => ProductName;
        public string Backend => "Windows HID (USB)";
        public bool IsConnected => _connected && !_stop;
        public int ChannelCount => _channels.Length;
        public string ChannelName(int index) => index >= 0 && index < _channels.Length ? _channels[index].Name : "-";
        public float Read(int index) => index >= 0 && index < _values.Length && IsConnected ? _values[index] : 0f;

        /// <summary>
        /// Opens the first HID joystick/gamepad that isn't an Xbox (XInput) pad or a device the Input System already
        /// treats as a gamepad, or returns null.
        /// </summary>
        public static WindowsHidChannelReader OpenFirst(ICollection<int> excludeVidPid = null)
        {
            foreach (string path in DevicePaths())
            {
                if (path.IndexOf("&ig_", StringComparison.OrdinalIgnoreCase) >= 0) continue; // XInput pad
                var reader = TryOpen(path, excludeVidPid);
                if (reader != null) return reader;
            }
            return null;
        }

        private static WindowsHidChannelReader TryOpen(string path, ICollection<int> excludeVidPid)
        {
            IntPtr h = CreateFile(path, GenericRead, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
            if (h == InvalidHandle) return null;
            IntPtr preparsed = IntPtr.Zero;
            try
            {
                var attr = new HiddAttributes { Size = (uint)sizeof(HiddAttributes) };
                if (!HidD_GetAttributes(h, ref attr)) return Fail();
                if (excludeVidPid != null && excludeVidPid.Contains(attr.VendorID << 16 | attr.ProductID)) return Fail();
                if (!HidD_GetPreparsedData(h, out preparsed)) return Fail();

                byte* caps = stackalloc byte[64];
                if (HidP_GetCaps(preparsed, caps) != HidpStatusSuccess) return Fail();
                ushort usage = *(ushort*)caps, page = *(ushort*)(caps + 2), inputLength = *(ushort*)(caps + 4);
                // Generic Desktop: 4 joystick, 5 game pad, 8 multi-axis controller.
                if (page != GenericDesktopPage || (usage != 4 && usage != 5 && usage != 8) || inputLength == 0) return Fail();

                char* name = stackalloc char[128];
                string product = HidD_GetProductString(h, name, 254) ? new string(name).TrimEnd('\0', ' ') : "";
                if (product.Length == 0) product = $"HID {attr.VendorID:X4}:{attr.ProductID:X4}";
                return new WindowsHidChannelReader(path, h, preparsed, inputLength, product, attr.VendorID, attr.ProductID);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GateRush][Input] Windows HID open failed for {path}: {e.Message}");
                return Fail();
            }

            WindowsHidChannelReader Fail()
            {
                if (preparsed != IntPtr.Zero) HidD_FreePreparsedData(preparsed);
                CloseHandle(h);
                return null;
            }
        }

        private void ReadValueChannels(List<Channel> list)
        {
            byte* caps = stackalloc byte[64];
            HidP_GetCaps(_preparsed, caps);
            ushort count = *(ushort*)(caps + 48);
            if (count == 0) return;
            var buf = new byte[count * CapsSize];
            var used = new Dictionary<string, int>();
            fixed (byte* b = buf)
            {
                if (HidP_GetValueCaps(HidpInput, b, ref count, _preparsed) != HidpStatusSuccess) return;
                for (int i = 0; i < count; i++)
                {
                    byte* c = b + i * CapsSize;
                    ushort page = *(ushort*)c, link = *(ushort*)(c + 6), bits = *(ushort*)(c + 18);
                    bool isRange = c[12] != 0;
                    int min = *(int*)(c + 40), max = *(int*)(c + 44);
                    ushort first = *(ushort*)(c + 56), last = isRange ? *(ushort*)(c + 58) : first;
                    if (max <= min) { min = 0; max = bits >= 31 ? int.MaxValue : (1 << bits) - 1; } // unsigned field with a bogus range
                    for (int u = first; u <= last; u++)
                    {
                        string name = AxisName(page, (ushort)u);
                        if (name == null) continue;
                        used.TryGetValue(name, out int n);
                        used[name] = n + 1;
                        list.Add(new Channel
                        {
                            Name = n == 0 ? name : name + n,
                            Page = page, Usage = (ushort)u, Link = link, Min = min, Max = max, Bits = bits,
                            Invert = name == "y" || name == "ry",
                        });
                    }
                }
            }
        }

        private void ReadButtonChannels(List<Channel> list)
        {
            byte* caps = stackalloc byte[64];
            HidP_GetCaps(_preparsed, caps);
            ushort count = *(ushort*)(caps + 46);
            if (count == 0) return;
            var buf = new byte[count * CapsSize];
            fixed (byte* b = buf)
            {
                if (HidP_GetButtonCaps(HidpInput, b, ref count, _preparsed) != HidpStatusSuccess) return;
                for (int i = 0; i < count; i++)
                {
                    byte* c = b + i * CapsSize;
                    if (*(ushort*)c != ButtonPage) continue;
                    ushort first = *(ushort*)(c + 56), last = c[12] != 0 ? *(ushort*)(c + 58) : first;
                    for (int u = first; u <= last && u > 0; u++)
                        list.Add(new Channel { Name = u == 1 ? "trigger" : "button" + u, Page = ButtonPage, Usage = (ushort)u, Button = true });
                }
            }
        }

        private static string AxisName(ushort page, ushort usage)
        {
            if (page == GenericDesktopPage)
            {
                switch (usage)
                {
                    case 0x30: return "x";
                    case 0x31: return "y";
                    case 0x32: return "z";
                    case 0x33: return "rx";
                    case 0x34: return "ry";
                    case 0x35: return "rz";
                    case 0x36: return "slider";
                    case 0x37: return "dial";
                    case 0x38: return "wheel";
                    case 0x39: return null; // hat switch: not a radio channel
                }
            }
            if (page == 0x02) // Simulation Controls
            {
                if (usage == 0xBA) return "rudder";
                if (usage == 0xBB) return "throttle";
            }
            return $"axis{page:X2}_{usage:X2}";
        }

        private static int Rank(string name)
        {
            int i = Array.IndexOf(AxisOrder, name);
            return i < 0 ? AxisOrder.Length : i;
        }

        private void Run()
        {
            IntPtr evt = CreateEvent(IntPtr.Zero, true, false, null);
            var ov = (NativeOverlapped*)Marshal.AllocHGlobal(sizeof(NativeOverlapped));
            var report = (byte*)Marshal.AllocHGlobal(_reportLength);
            try
            {
                while (!_stop)
                {
                    *ov = default;
                    ov->EventHandle = evt;
                    ResetEvent(evt);
                    if (!ReadFile(_handle, report, (uint)_reportLength, IntPtr.Zero, ov))
                    {
                        if (Marshal.GetLastWin32Error() != ErrorIoPending) break;
                        while (!_stop && WaitForSingleObject(evt, 100) == WaitTimeout) { }
                        if (_stop)
                        {
                            CancelIo(_handle);
                            GetOverlappedResult(_handle, ov, out _, true);
                            break;
                        }
                    }
                    if (!GetOverlappedResult(_handle, ov, out uint read, false)) break;
                    Decode(report, read);
                    _reports++;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[GateRush][Input] Windows HID read stopped: " + e.Message);
            }
            finally
            {
                _connected = false;
                Marshal.FreeHGlobal((IntPtr)report);
                Marshal.FreeHGlobal((IntPtr)ov);
                CloseHandle(evt);
            }
        }

        private void Decode(byte* report, uint length)
        {
            bool buttonsRead = false;
            fixed (ushort* usages = _usageBuffer)
            {
                for (int i = 0; i < _channels.Length; i++)
                {
                    ref readonly var ch = ref _channels[i];
                    if (ch.Button)
                    {
                        if (buttonsRead) continue;
                        buttonsRead = true;
                        uint n = (uint)_usageBuffer.Length;
                        // Reports with another report ID don't carry the buttons: keep the previous state.
                        if (HidP_GetUsages(HidpInput, ButtonPage, 0, usages, ref n, _preparsed, report, length) != HidpStatusSuccess) continue;
                        for (int j = i; j < _channels.Length; j++) _values[j] = 0f;
                        for (int k = 0; k < n; k++)
                            for (int j = i; j < _channels.Length; j++)
                                if (_channels[j].Usage == usages[k]) _values[j] = 1f;
                        continue;
                    }
                    if (HidP_GetUsageValue(HidpInput, ch.Page, ch.Link, ch.Usage, out uint raw, _preparsed, report, length) != HidpStatusSuccess) continue;
                    long v = raw;
                    if (ch.Min < 0 && ch.Bits > 0 && ch.Bits < 32 && (raw & (1u << (ch.Bits - 1))) != 0) v -= 1L << ch.Bits; // sign-extend
                    float f = (float)((v - (double)ch.Min) / ((double)ch.Max - ch.Min) * 2.0 - 1.0);
                    f = f < -1f ? -1f : f > 1f ? 1f : f;
                    _values[i] = ch.Invert ? -f : f;
                }
            }
        }

        public void Dispose()
        {
            if (_stop) return;
            _stop = true;
            if (_thread.IsAlive && !_thread.Join(1000)) Debug.LogWarning("[GateRush][Input] Windows HID reader thread did not stop");
            CloseHandle(_handle);
            HidD_FreePreparsedData(_preparsed);
        }

        private static List<string> DevicePaths()
        {
            var list = new List<string>();
            HidD_GetHidGuid(out Guid guid);
            IntPtr set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
            if (set == InvalidHandle) return list;
            try
            {
                for (int i = 0; ; i++)
                {
                    var did = new SpDeviceInterfaceData { cbSize = sizeof(SpDeviceInterfaceData) };
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref did)) break;
                    SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out int size, IntPtr.Zero);
                    if (size <= 0) continue;
                    IntPtr detail = Marshal.AllocHGlobal(size);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6); // sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA_W)
                        if (SetupDiGetDeviceInterfaceDetail(set, ref did, detail, size, out size, IntPtr.Zero))
                            list.Add(Marshal.PtrToStringUni(detail + 4));
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detail);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
            return list;
        }

        // --- Win32 ---

        private static readonly IntPtr InvalidHandle = new IntPtr(-1);
        private const uint GenericRead = 0x80000000;
        private const uint FileShareRead = 1, FileShareWrite = 2;
        private const uint OpenExisting = 3;
        private const uint FileFlagOverlapped = 0x40000000;
        private const int DigcfPresent = 0x02, DigcfDeviceInterface = 0x10;

        [StructLayout(LayoutKind.Sequential)]
        private struct SpDeviceInterfaceData
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HiddAttributes
        {
            public uint Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetAttributes(IntPtr h, ref HiddAttributes attributes);
        [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetProductString(IntPtr h, char* buffer, uint bufferBytes);
        [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr preparsed);
        [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_FreePreparsedData(IntPtr preparsed);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr preparsed, byte* caps);
        [DllImport("hid.dll")] private static extern int HidP_GetValueCaps(int reportType, byte* caps, ref ushort length, IntPtr preparsed);
        [DllImport("hid.dll")] private static extern int HidP_GetButtonCaps(int reportType, byte* caps, ref ushort length, IntPtr preparsed);
        [DllImport("hid.dll")] private static extern int HidP_GetUsageValue(int reportType, ushort usagePage, ushort link, ushort usage, out uint value, IntPtr preparsed, byte* report, uint length);
        [DllImport("hid.dll")] private static extern int HidP_GetUsages(int reportType, ushort usagePage, ushort link, ushort* usages, ref uint count, IntPtr preparsed, byte* report, uint length);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string enumerator, IntPtr parent, int flags);
        [DllImport("setupapi.dll")] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, int index, ref SpDeviceInterfaceData data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SpDeviceInterfaceData data, IntPtr detail, int size, out int required, IntPtr info);
        [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadFile(IntPtr h, byte* buffer, uint length, IntPtr read, NativeOverlapped* overlapped);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetOverlappedResult(IntPtr h, NativeOverlapped* overlapped, out uint transferred, bool wait);
        [DllImport("kernel32.dll")] private static extern bool CancelIo(IntPtr h);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateEvent(IntPtr security, bool manualReset, bool initialState, string name);
        [DllImport("kernel32.dll")] private static extern bool ResetEvent(IntPtr h);
        [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr h, uint milliseconds);
    }
}
#endif
