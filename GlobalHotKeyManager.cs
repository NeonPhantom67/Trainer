using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace RVTrainer
{
    // Virtual key codes for hotkeys
    public enum VirtualKeys : uint
    {
        F1 = 0x70,
        F2 = 0x71,
        F3 = 0x72,
        F4 = 0x73,
        F5 = 0x74,
        F6 = 0x75,
        F7 = 0x76,
        F8 = 0x77,
        F9 = 0x78,
        F10 = 0x79,
        F11 = 0x7A,
        F12 = 0x7B
    }
    
    internal class GlobalHotKeyManager
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public const int WM_HOTKEY = 0x0312;

        // Modifiers
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        private readonly Dictionary<int, Action> _actions = new();
        private IntPtr _handle = IntPtr.Zero;
        private int _nextId = 9000;

        public bool Register(IntPtr handle, VirtualKeys key, uint modifiers, Action action)
        {
            if (_handle == IntPtr.Zero)
                _handle = handle;

            int id = _nextId++;
            uint vk = (uint)key;
            
            if (RegisterHotKey(_handle, id, modifiers | MOD_NOREPEAT, vk))
            {
                _actions[id] = action;
                DebugLogger.Log($"Registered global hotkey: ID={id}, Key={key}, Modifiers={modifiers:X}", LogLevel.Info);
                return true;
            }
            else
            {
                int error = Marshal.GetLastWin32Error();
                DebugLogger.Log($"Failed to register hotkey {key}: Error {error}", LogLevel.Error);
                return false;
            }
        }

        public void HandleHotKey(int id)
        {
            if (_actions.TryGetValue(id, out Action? action))
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    DebugLogger.LogError($"Hotkey action {id}", ex);
                }
            }
        }

        public void UnregisterAll()
        {
            foreach (var id in _actions.Keys)
            {
                UnregisterHotKey(_handle, id);
            }
            _actions.Clear();
            DebugLogger.Log("Unregistered all global hotkeys", LogLevel.Info);
        }
    }
}
