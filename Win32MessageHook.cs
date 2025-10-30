using Avalonia;
using Avalonia.Controls;
using System;
using System.Runtime.InteropServices;

namespace RVTrainer
{
    internal class Win32MessageHook
    {
        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const int GWL_WNDPROC = -4;
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        
        private IntPtr _oldWndProc = IntPtr.Zero;
        private WndProcDelegate? _newWndProc;
        private Action<int>? _hotKeyHandler;

        public void InstallHook(Window window, Action<int> hotKeyHandler)
        {
            _hotKeyHandler = hotKeyHandler;
            
            var platformHandle = window.TryGetPlatformHandle();
            if (platformHandle == null || platformHandle.Handle == IntPtr.Zero)
            {
                DebugLogger.Log("Failed to get platform handle for message hook", LogLevel.Error);
                return;
            }

            IntPtr hwnd = platformHandle.Handle;
            
            _newWndProc = new WndProcDelegate(WndProc);
            _oldWndProc = SetWindowLongPtr(hwnd, GWL_WNDPROC, Marshal.GetFunctionPointerForDelegate(_newWndProc));
            
            if (_oldWndProc != IntPtr.Zero)
            {
                DebugLogger.Log("Win32 message hook installed", LogLevel.Info);
            }
            else
            {
                DebugLogger.Log("Failed to install Win32 message hook", LogLevel.Error);
            }
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == GlobalHotKeyManager.WM_HOTKEY)
            {
                int hotkeyId = (int)wParam;
                _hotKeyHandler?.Invoke(hotkeyId);
            }

            return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
        }
    }
}
