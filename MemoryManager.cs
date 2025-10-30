using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RVTrainer
{
    internal class MemoryManager : IDisposable
    {
        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out int lpNumberOfBytesRead);

        [DllImport("kernel32.dll")]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out int lpNumberOfBytesWritten);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtectEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flNewProtect, out uint lpflOldProtect);

        private const int PROCESS_ALL_ACCESS = 0x1F0FFF;
        private const uint PAGE_EXECUTE_READWRITE = 0x40;

        private IntPtr processHandle = IntPtr.Zero;
        private Process gameProcess = null;
        private IntPtr baseAddress = IntPtr.Zero;

        public bool IsAttached => processHandle != IntPtr.Zero && gameProcess != null && !gameProcess.HasExited;
        public IntPtr ProcessHandle => processHandle;
        public Process GameProcess => gameProcess;

        public bool AttachToGame(string processName)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName.Replace(".exe", ""));
                if (processes.Length == 0) return false;

                gameProcess = processes[0];
                processHandle = OpenProcess(PROCESS_ALL_ACCESS, false, gameProcess.Id);
                
                if (processHandle == IntPtr.Zero) return false;

                if (gameProcess.MainModule == null)
                {
                    DebugLogger.Log("Failed to get main module", LogLevel.Error);
                    return false;
                }

                baseAddress = gameProcess.MainModule.BaseAddress;
                DebugLogger.Log($"Attached to {processName} (PID: {gameProcess.Id})", LogLevel.Info);
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("AttachToGame", ex);
                return false;
            }
        }

        public void Detach()
        {
            if (processHandle != IntPtr.Zero)
            {
                CloseHandle(processHandle);
                processHandle = IntPtr.Zero;
            }
            gameProcess?.Dispose();
            gameProcess = null;
        }

        public void Dispose()
        {
            Detach();
        }

        public bool ReadFloat(IntPtr address, out float value)
        {
            value = 0f;
            byte[] buffer = new byte[4];
            
            if (ReadProcessMemory(processHandle, address, buffer, 4, out int bytesRead) && bytesRead == 4)
            {
                value = BitConverter.ToSingle(buffer, 0);
                return true;
            }
            return false;
        }

        public bool WriteFloat(IntPtr address, float value)
        {
            byte[] buffer = BitConverter.GetBytes(value);
            return WriteBytes(address, buffer);
        }

        public bool ReadDouble(IntPtr address, out double value)
        {
            value = 0.0;
            byte[] buffer = new byte[8];
            
            if (ReadProcessMemory(processHandle, address, buffer, 8, out int bytesRead) && bytesRead == 8)
            {
                value = BitConverter.ToDouble(buffer, 0);
                return true;
            }
            return false;
        }

        public bool WriteDouble(IntPtr address, double value)
        {
            byte[] buffer = BitConverter.GetBytes(value);
            return WriteBytes(address, buffer);
        }

        public bool ReadInt32(IntPtr address, out int value)
        {
            value = 0;
            byte[] buffer = new byte[4];
            
            if (ReadProcessMemory(processHandle, address, buffer, 4, out int bytesRead) && bytesRead == 4)
            {
                value = BitConverter.ToInt32(buffer, 0);
                return true;
            }
            return false;
        }

        public bool WriteInt32(IntPtr address, int value)
        {
            byte[] buffer = BitConverter.GetBytes(value);
            return WriteBytes(address, buffer);
        }

        public bool ReadPointer(IntPtr address, out IntPtr pointer)
        {
            pointer = IntPtr.Zero;
            byte[] buffer = new byte[IntPtr.Size];
            
            if (ReadProcessMemory(processHandle, address, buffer, IntPtr.Size, out int bytesRead) && bytesRead == IntPtr.Size)
            {
                pointer = IntPtr.Size == 8 
                    ? new IntPtr(BitConverter.ToInt64(buffer, 0)) 
                    : new IntPtr(BitConverter.ToInt32(buffer, 0));
                return true;
            }
            return false;
        }

        public IntPtr GetAddressFromPointerChain(IntPtr baseAddr, params int[] offsets)
        {
            IntPtr currentAddress = baseAddr;

            for (int i = 0; i < offsets.Length; i++)
            {
                if (i < offsets.Length - 1)
                {
                    IntPtr readAddr = currentAddress + offsets[i];
                    
                    if (!ReadPointer(readAddr, out IntPtr nextAddr))
                    {
                        return IntPtr.Zero;
                    }
                    
                    if (nextAddr == IntPtr.Zero || nextAddr.ToInt64() < 0x1000)
                    {
                        return IntPtr.Zero;
                    }
                    
                    currentAddress = nextAddr;
                }
                else
                {
                    currentAddress = currentAddress + offsets[i];
                }
            }

            return currentAddress;
        }

        public IntPtr FindPattern(string pattern)
        {
            if (!IsAttached || baseAddress == IntPtr.Zero)
                return IntPtr.Zero;

            try
            {
                byte[] patternBytes = ParsePattern(pattern);
                if (patternBytes == null || patternBytes.Length == 0)
                    return IntPtr.Zero;

                ProcessModule? mainModule = gameProcess.MainModule;
                if (mainModule == null)
                {
                    DebugLogger.Log("Main module is null in FindPattern", LogLevel.Error);
                    return IntPtr.Zero;
                }
                IntPtr moduleBase = mainModule.BaseAddress;
                int moduleSize = mainModule.ModuleMemorySize;

                byte[] moduleMemory = new byte[moduleSize];
                if (!ReadProcessMemory(processHandle, moduleBase, moduleMemory, moduleSize, out int bytesRead))
                    return IntPtr.Zero;

                for (int i = 0; i < moduleMemory.Length - patternBytes.Length; i++)
                {
                    bool found = true;
                    for (int j = 0; j < patternBytes.Length; j++)
                    {
                        if (patternBytes[j] != 0xFF && moduleMemory[i + j] != patternBytes[j])
                        {
                            found = false;
                            break;
                        }
                    }

                    if (found)
                        return IntPtr.Add(moduleBase, i);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("FindPattern", ex);
            }

            return IntPtr.Zero;
        }

        private byte[] ParsePattern(string pattern)
        {
            try
            {
                pattern = pattern.Replace(" ", "").Replace("0x", "");
                var result = new System.Collections.Generic.List<byte>();

                for (int i = 0; i < pattern.Length; i += 2)
                {
                    if (i + 1 >= pattern.Length) break;
                        
                    string byteString = pattern.Substring(i, 2);
                    if (byteString == "??" || byteString.ToLower() == "xx")
                    {
                        result.Add(0xFF);
                    }
                    else
                    {
                        result.Add(Convert.ToByte(byteString, 16));
                    }
                }

                return result.ToArray();
            }
            catch
            {
                return null;
            }
        }

        public bool ReadBytes(IntPtr address, int size, out byte[] buffer)
        {
            buffer = new byte[size];
            return ReadProcessMemory(processHandle, address, buffer, size, out int bytesRead) && bytesRead == size;
        }

        public bool WriteBytes(IntPtr address, byte[] buffer)
        {
            if (VirtualProtectEx(processHandle, address, (uint)buffer.Length, PAGE_EXECUTE_READWRITE, out uint oldProtect))
            {
                bool result = WriteProcessMemory(processHandle, address, buffer, buffer.Length, out int written) && written == buffer.Length;
                VirtualProtectEx(processHandle, address, (uint)buffer.Length, oldProtect, out _);
                return result;
            }
            
            return WriteProcessMemory(processHandle, address, buffer, buffer.Length, out int bytesWritten) && bytesWritten == buffer.Length;
        }
    }
}
