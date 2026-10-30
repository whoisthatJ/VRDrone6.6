using System.Threading;
using UnityEngine;

namespace GateRush.Input
{
    /// <summary>
    /// C# side of the Android raw-joystick fallback (Plugins/Android/GateRushRadio.androidlib, GateRushJoystick.java).
    /// Java writes MotionEvent axes (including AXIS_GENERIC_1..16) and button bits into a direct ByteBuffer;
    /// <see cref="Poll"/> copies each slot once per frame through a pointer, using the slot's sequence counter
    /// to skip torn writes. No locks, no JNI calls and no allocations per frame.
    /// </summary>
    public static unsafe class AndroidJoystickBridge
    {
        public const int MaxDevices = 4;
        public const int MaxAxes = 48;
        private const int HeaderInts = 8;
        private const int SlotInts = HeaderInts + MaxAxes;

        public struct Slot
        {
            public int DeviceId;
            public int VendorId;
            public int ProductId;
            public ulong AxisMask;
            public uint Buttons;
            public fixed float Axes[MaxAxes];

            public bool Present => DeviceId != 0;

            public int AxisCount
            {
                get
                {
                    int n = 0;
                    for (int a = 0; a < MaxAxes; a++) if ((AxisMask & (1UL << a)) != 0) n++;
                    return n;
                }
            }
        }

        private static byte* _buffer;
        private static bool _tried;
        private static readonly Slot[] Slots = new Slot[MaxDevices];
        private static readonly string[] Names = new string[MaxDevices];
        private static readonly int[] NamedDevice = new int[MaxDevices];

        /// <summary>True on an Android device once the Java hook is installed.</summary>
        public static bool Available => _buffer != null;

        /// <summary>Installs the Java hook (once). Safe to call anywhere; does nothing off-device.</summary>
        public static bool Install()
        {
            if (_tried) return Available;
            _tried = true;
#if UNITY_ANDROID
            if (Application.platform != RuntimePlatform.Android) return false;
            try
            {
                using var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unity.GetStatic<AndroidJavaObject>("currentActivity");
                using var cls = new AndroidJavaClass("com.gaterush.input.GateRushJoystick");
                var buffer = cls.CallStatic<AndroidJavaObject>("install", activity);
                _buffer = (byte*)(void*)AndroidJNI.GetDirectBufferAddress(buffer.GetRawObject());
                Debug.Log($"[GateRush][Input] Android raw joystick bridge {(Available ? "installed" : "unavailable")}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[GateRush][Input] Android raw joystick bridge failed: " + e.Message);
                _buffer = null;
            }
#endif
            return Available;
        }

        /// <summary>Copies all slots out of the shared buffer. Call once per frame.</summary>
        public static void Poll()
        {
            if (_buffer == null) return;
            for (int s = 0; s < MaxDevices; s++)
            {
                int* p = (int*)(_buffer + s * SlotInts * 4);
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    int seq = Volatile.Read(ref p[0]);
                    if ((seq & 1) != 0) continue; // writer active
                    var slot = new Slot
                    {
                        DeviceId = p[1],
                        VendorId = p[2],
                        ProductId = p[3],
                        AxisMask = (uint)p[4] | ((ulong)(uint)p[5] << 32),
                        Buttons = (uint)p[6],
                    };
                    float* axes = (float*)(p + HeaderInts);
                    for (int a = 0; a < MaxAxes; a++) slot.Axes[a] = axes[a];
                    if (Volatile.Read(ref p[0]) != seq) continue; // torn read, retry
                    Slots[s] = slot;
                    break;
                }
            }
        }

        public static ref readonly Slot Get(int slot) => ref Slots[slot];

        /// <summary>Latest polled value of one AXIS_* index for a slot.</summary>
        public static float Axis(int slot, int axis)
        {
            fixed (Slot* p = &Slots[slot]) return p->Axes[axis];
        }

        /// <summary>Device name for a slot (JNI call, cached until the slot's device changes).</summary>
        public static string Name(int slot)
        {
            int id = Slots[slot].DeviceId;
            if (id == 0) return "";
            if (NamedDevice[slot] == id && Names[slot] != null) return Names[slot];
#if UNITY_ANDROID
            if (Application.platform == RuntimePlatform.Android)
            {
                using var cls = new AndroidJavaClass("com.gaterush.input.GateRushJoystick");
                Names[slot] = cls.CallStatic<string>("getDeviceName", slot);
                NamedDevice[slot] = id;
                return Names[slot];
            }
#endif
            return "Joystick " + id;
        }
    }

    /// <summary>Channels of one Android raw slot: every axis the device reports (in AXIS_* order), then 32 buttons.</summary>
    public sealed class AndroidChannelReader : IChannelReader
    {
        private static readonly string[] AxisNames =
        {
            "X", "Y", "PRESSURE", "SIZE", "TOUCH_MAJOR", "TOUCH_MINOR", "TOOL_MAJOR", "TOOL_MINOR", "ORIENTATION", "VSCROLL",
            "HSCROLL", "Z", "RX", "RY", "RZ", "HAT_X", "HAT_Y", "LTRIGGER", "RTRIGGER", "THROTTLE", "RUDDER", "WHEEL", "GAS",
            "BRAKE", "DISTANCE", "TILT", "SCROLL", "REL_X", "REL_Y", "29", "30", "31",
            "GENERIC_1", "GENERIC_2", "GENERIC_3", "GENERIC_4", "GENERIC_5", "GENERIC_6", "GENERIC_7", "GENERIC_8",
            "GENERIC_9", "GENERIC_10", "GENERIC_11", "GENERIC_12", "GENERIC_13", "GENERIC_14", "GENERIC_15", "GENERIC_16",
        };

        private readonly int _slot;
        private readonly int _deviceId;
        private readonly int[] _axes;

        public AndroidChannelReader(int slot)
        {
            _slot = slot;
            ref readonly var s = ref AndroidJoystickBridge.Get(slot);
            _deviceId = s.DeviceId;
            _axes = new int[s.AxisCount];
            int n = 0;
            for (int a = 0; a < AndroidJoystickBridge.MaxAxes; a++)
                if ((s.AxisMask & (1UL << a)) != 0) _axes[n++] = a;
            ProductName = AndroidJoystickBridge.Name(slot);
            DeviceKey = $"{ProductName}|{s.VendorId:X4}|{s.ProductId:X4}";
        }

        public int Slot => _slot;
        public int AxisCount => _axes.Length;
        public string DeviceKey { get; }
        public string ProductName { get; }
        public string DisplayName => ProductName;
        public string Backend => "Android raw";
        public bool IsConnected => AndroidJoystickBridge.Get(_slot).DeviceId == _deviceId;
        public int ChannelCount => _axes.Length + 32;

        public string ChannelName(int index)
        {
            if (index < 0) return "-";
            if (index < _axes.Length) return "AXIS_" + AxisNames[_axes[index]];
            return index - _axes.Length < 32 ? "BUTTON_" + (index - _axes.Length) : "-";
        }

        public float Read(int index)
        {
            ref readonly var s = ref AndroidJoystickBridge.Get(_slot);
            if (s.DeviceId != _deviceId || index < 0) return 0f;
            if (index < _axes.Length) return AndroidJoystickBridge.Axis(_slot, _axes[index]);
            int bit = index - _axes.Length;
            return bit < 32 && (s.Buttons & (1u << bit)) != 0 ? 1f : 0f;
        }
    }
}
