using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace GateRush.Core
{
    /// <summary>
    /// Window mode and resolution of the Windows build (<see cref="GraphicsPrefs.DisplayMode"/>, Resolution*). Applied
    /// at startup from the saved settings, so the game always opens the way the player left it (fullscreen by default),
    /// whatever Unity remembered in the registry. Does nothing in the editor.
    /// </summary>
    public static class ScreenDisplay
    {
        /// <summary>Names for <see cref="GraphicsPrefs.DisplayMode"/>, in order.</summary>
        public static readonly string[] Modes = { "Fullscreen", "Exclusive fullscreen", "Maximized window", "Window" };

        public const int Fullscreen = 0, Exclusive = 1, Maximized = 2, Windowed = 3;

        /// <summary>Distinct resolutions the monitor offers, largest first.</summary>
        public static List<Vector2Int> Resolutions()
        {
            var list = new List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                var v = new Vector2Int(r.width, r.height);
                if (v.x >= 800 && !list.Contains(v)) list.Add(v);
            }
            list.Sort((a, b) => b.x != a.x ? b.x.CompareTo(a.x) : b.y.CompareTo(a.y));
            return list;
        }

        /// <summary>The monitor's own (desktop) resolution.</summary>
        public static Vector2Int Native => new Vector2Int(Display.main.systemWidth, Display.main.systemHeight);

        /// <summary>Size the game renders at for these prefs: the chosen resolution, or native (a window gets 75 %).</summary>
        public static Vector2Int TargetSize(GraphicsPrefs prefs, Vector2Int native)
        {
            if (prefs.ResolutionWidth > 0 && prefs.ResolutionHeight > 0 && prefs.DisplayMode != Maximized)
                return new Vector2Int(Mathf.Min(prefs.ResolutionWidth, native.x), Mathf.Min(prefs.ResolutionHeight, native.y));
            if (prefs.DisplayMode == Windowed) return new Vector2Int(native.x * 3 / 4, native.y * 3 / 4);
            return native;
        }

        public static void Apply(GraphicsPrefs prefs)
        {
            if (Application.isEditor) return;
            var size = TargetSize(prefs, Native);
            switch (prefs.DisplayMode)
            {
                case Exclusive:
                    Screen.SetResolution(size.x, size.y, FullScreenMode.ExclusiveFullScreen);
                    break;
                case Maximized:
                    // Unity's MaximizedWindow is macOS-only: open a window, then let Windows maximize it.
                    Screen.SetResolution(size.x * 3 / 4, size.y * 3 / 4, FullScreenMode.Windowed);
                    _maximizeFrames = 3;
                    EnsureRunner();
                    break;
                case Windowed:
                    Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);
                    break;
                default:
                    Screen.SetResolution(size.x, size.y, FullScreenMode.FullScreenWindow);
                    break;
            }
        }

        // ---------- maximize (Win32) ----------

        private static int _maximizeFrames;
        private static GameObject _runner;

        private static void EnsureRunner()
        {
            if (_runner != null) return;
            _runner = new GameObject("ScreenDisplay") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<Runner>();
        }

        /// <summary>Waits a few frames for the window mode switch, then maximizes the window.</summary>
        private sealed class Runner : MonoBehaviour
        {
            private void Update()
            {
                if (_maximizeFrames <= 0 || --_maximizeFrames > 0) return;
                if (Screen.fullScreenMode == FullScreenMode.Windowed) Maximize();
            }
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GwlStyle = -16, WsMaximizeBox = 0x00010000, WsThickFrame = 0x00040000, SwMaximize = 3;

        private static void Maximize()
        {
            IntPtr hwnd = GetActiveWindow();
            if (hwnd == IntPtr.Zero) return;
            SetWindowLong(hwnd, GwlStyle, GetWindowLong(hwnd, GwlStyle) | WsMaximizeBox | WsThickFrame);
            ShowWindow(hwnd, SwMaximize);
        }
#else
        private static void Maximize() { }
#endif
    }
}
