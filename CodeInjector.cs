using System;
using System.Runtime.InteropServices;

namespace RVTrainer
{
    internal class CodeInjector
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtectEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flNewProtect, out uint lpflOldProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out int lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out int lpNumberOfBytesRead);

        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RESERVE = 0x2000;
        private const uint PAGE_EXECUTE_READWRITE = 0x40;
        private const uint PAGE_READWRITE = 0x04;

        private readonly IntPtr processHandle;
        private readonly MemoryManager memory;

        public CodeInjector(IntPtr handle, MemoryManager mem)
        {
            processHandle = handle;
            memory = mem;
        }

        // Allocate code cave near target address
        public IntPtr AllocateCodeCave(IntPtr nearAddress, int size)
        {
            // Try to allocate within 2GB of target (for relative jumps)
            long targetAddr = nearAddress.ToInt64();
            
            for (long offset = 0x10000; offset < 0x7FFFFFFF; offset += 0x10000)
            {
                IntPtr addr = new IntPtr(targetAddr + offset);
                IntPtr allocated = VirtualAllocEx(processHandle, addr, (uint)size, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
                
                if (allocated != IntPtr.Zero)
                {
                    DebugLogger.Log($"Allocated code cave at: 0x{allocated.ToInt64():X}");
                    return allocated;
                }
                
                // Try below target too
                addr = new IntPtr(targetAddr - offset);
                allocated = VirtualAllocEx(processHandle, addr, (uint)size, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
                
                if (allocated != IntPtr.Zero)
                {
                    DebugLogger.Log($"Allocated code cave at: 0x{allocated.ToInt64():X}");
                    return allocated;
                }
            }

            DebugLogger.Log("Failed to allocate code cave", LogLevel.Error);
            return IntPtr.Zero;
        }

        // Create a detour (hook) at target address
        public bool CreateDetour(IntPtr targetAddress, byte[] hookCode, out byte[] originalBytes)
        {
            originalBytes = new byte[hookCode.Length];

            // Read original bytes
            if (!ReadProcessMemory(processHandle, targetAddress, originalBytes, hookCode.Length, out _))
            {
                DebugLogger.Log($"Failed to read original bytes at 0x{targetAddress.ToInt64():X}", LogLevel.Error);
                return false;
            }

            // Change protection to writable
            if (!VirtualProtectEx(processHandle, targetAddress, (uint)hookCode.Length, PAGE_EXECUTE_READWRITE, out uint oldProtect))
            {
                DebugLogger.Log($"Failed to change protection at 0x{targetAddress.ToInt64():X}", LogLevel.Error);
                return false;
            }

            // Write hook
            if (!WriteProcessMemory(processHandle, targetAddress, hookCode, hookCode.Length, out _))
            {
                DebugLogger.Log($"Failed to write hook at 0x{targetAddress.ToInt64():X}", LogLevel.Error);
                return false;
            }

            // Restore protection
            VirtualProtectEx(processHandle, targetAddress, (uint)hookCode.Length, oldProtect, out _);

            DebugLogger.Log($"✓ Created detour at 0x{targetAddress.ToInt64():X}", LogLevel.Info);
            return true;
        }

        // Generate x64 JMP instruction (14 bytes)
        public static byte[] GenerateAbsoluteJump(IntPtr destination)
        {
            byte[] jmp = new byte[14];
            jmp[0] = 0xFF; // JMP [RIP+0]
            jmp[1] = 0x25;
            jmp[2] = 0x00; // Offset = 0
            jmp[3] = 0x00;
            jmp[4] = 0x00;
            jmp[5] = 0x00;
            
            // Address (8 bytes)
            byte[] addr = BitConverter.GetBytes(destination.ToInt64());
            Array.Copy(addr, 0, jmp, 6, 8);
            
            return jmp;
        }

        // Generate relative JMP (5 bytes) if possible
        public static byte[] GenerateRelativeJump(IntPtr from, IntPtr to)
        {
            long offset = to.ToInt64() - (from.ToInt64() + 5);
            
            // Check if offset fits in 32-bit
            if (offset < int.MinValue || offset > int.MaxValue)
            {
                return null; // Use absolute jump instead
            }

            byte[] jmp = new byte[5];
            jmp[0] = 0xE9; // JMP rel32
            byte[] offsetBytes = BitConverter.GetBytes((int)offset);
            Array.Copy(offsetBytes, 0, jmp, 1, 4);
            
            return jmp;
        }
    }
}
