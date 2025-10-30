using System;
using System.Runtime.InteropServices;

namespace RVTrainer
{
    internal class PointerCapture
    {
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        public struct CapturedPointers
        {
            public long GameStatePtr;      // 0x00 - Captured from RCX
            public long GameInstancePtr;   // 0x08 - Captured from RDI  
            public long BaseObjectPtr;     // 0x10 - Captured from RAX (common base)
            public int IsValid;            // 0x18 - Unused for now
        }

        private IntPtr sharedMemory = IntPtr.Zero;
        private readonly CodeInjector injector;
        private readonly MemoryManager memory;
        private readonly IntPtr processHandle;

        public PointerCapture(IntPtr handle, MemoryManager mem, CodeInjector inj)
        {
            processHandle = handle;
            memory = mem;
            injector = inj;
        }

        // Setup hooks to capture GameState and GameInstance pointers
        public bool SetupCapture(IntPtr gstatePattern, IntPtr ginstancePattern)
        {
            DebugLogger.Log("Setting up pointer capture hooks...");

            // Use whichever pattern is valid for allocating shared memory
            IntPtr allocNear = gstatePattern != IntPtr.Zero ? gstatePattern : ginstancePattern;
            if (allocNear == IntPtr.Zero)
            {
                DebugLogger.Log("No valid patterns provided for allocation", LogLevel.Error);
                return false;
            }

            // Allocate shared memory for captured pointers
            sharedMemory = injector.AllocateCodeCave(allocNear, 0x1000);
            if (sharedMemory == IntPtr.Zero)
            {
                return false;
            }

            // Zero out shared memory
            byte[] zeros = new byte[Marshal.SizeOf<CapturedPointers>()];
            memory.WriteBytes(sharedMemory, zeros);

            bool anyHookSucceeded = false;

            // Hook GSTATE pattern if available
            if (gstatePattern != IntPtr.Zero)
            {
                if (HookGameState(gstatePattern))
                {
                    anyHookSucceeded = true;
                }
                else
                {
                    DebugLogger.Log("Failed to hook GameState", LogLevel.Warning);
                }
            }
            else
            {
                DebugLogger.Log("GSTATE pattern not found - skipping GameState hook", LogLevel.Warning);
            }

            // Hook GINSTANCE pattern if available
            if (ginstancePattern != IntPtr.Zero)
            {
                if (HookGameInstance(ginstancePattern))
                {
                    anyHookSucceeded = true;
                }
                else
                {
                    DebugLogger.Log("Failed to hook GameInstance", LogLevel.Warning);
                }
            }
            else
            {
                DebugLogger.Log("GINSTANCE pattern not found - skipping GameInstance hook", LogLevel.Warning);
            }

            return anyHookSucceeded;
        }

        private bool HookGameState(IntPtr patternAddr)
        {
            // Original instruction: mov rcx,[rax+1B0]
            // We want to capture the VALUE in RCX after this instruction executes

            IntPtr hookAddr = patternAddr;
            IntPtr caveAddr = injector.AllocateCodeCave(hookAddr, 256);
            if (caveAddr == IntPtr.Zero) return false;

            // Build hook code
            var code = new System.Collections.Generic.List<byte>();
            
            // Save RAX first (the base object pointer) to shared memory + 16
            code.AddRange(new byte[] { 0x48, 0x89, 0x05 }); // mov [rip+offset], rax
            int raxOffset = (int)(sharedMemory.ToInt64() + 16 - (caveAddr.ToInt64() + code.Count + 4));
            code.AddRange(BitConverter.GetBytes(raxOffset));
            
            // Original instruction
            code.AddRange(new byte[] { 0x48, 0x8B, 0x88, 0xB0, 0x01, 0x00, 0x00 }); // mov rcx,[rax+1B0]
            
            // Save RCX (GameState) to shared memory + 0
            code.AddRange(new byte[] { 0x48, 0x89, 0x0D }); // mov [rip+offset], rcx
            int offset = (int)(sharedMemory.ToInt64() - (caveAddr.ToInt64() + code.Count + 4));
            code.AddRange(BitConverter.GetBytes(offset));
            
            // Jump back
            byte[] jmpBack = CodeInjector.GenerateRelativeJump(
                new IntPtr(caveAddr.ToInt64() + code.Count),
                new IntPtr(hookAddr.ToInt64() + 7)
            );
            if (jmpBack != null)
            {
                code.AddRange(jmpBack);
            }

            // Write cave code
            if (!memory.WriteBytes(caveAddr, code.ToArray()))
            {
                return false;
            }

            // Create jump to cave - MUST be exactly 7 bytes to match original instruction
            byte[] jmp = CodeInjector.GenerateRelativeJump(hookAddr, caveAddr);
            if (jmp == null)
            {
                // Absolute jump is 14 bytes - too large! Log error and fail
                DebugLogger.Log($"Cave too far from hook (>2GB). Cannot use 7-byte instruction space.", LogLevel.Error);
                return false;
            }
            
            // Pad with NOPs to exactly 7 bytes
            if (jmp.Length < 7)
            {
                byte[] padded = new byte[7];
                Array.Copy(jmp, padded, jmp.Length);
                for (int i = jmp.Length; i < 7; i++)
                {
                    padded[i] = 0x90; // NOP
                }
                jmp = padded;
            }

            return injector.CreateDetour(hookAddr, jmp, out _);
        }

        private bool HookGameInstance(IntPtr patternAddr)
        {
            // Hook GameInstance capture
            // Original: mov rdi,[rax+228]
            
            // Validate pattern address first
            if (patternAddr == IntPtr.Zero || patternAddr.ToInt64() < 0x10000)
            {
                DebugLogger.Log($"Invalid GINSTANCE pattern address: 0x{patternAddr.ToInt64():X}", LogLevel.Error);
                return false;
            }
            
            IntPtr hookAddr = patternAddr;
            IntPtr caveAddr = injector.AllocateCodeCave(hookAddr, 256);
            if (caveAddr == IntPtr.Zero)
            {
                DebugLogger.Log("Failed to allocate cave for GameInstance hook", LogLevel.Error);
                return false;
            }

            var code = new System.Collections.Generic.List<byte>();
            
            // Save RAX first (the base object pointer) to shared memory + 16
            code.AddRange(new byte[] { 0x48, 0x89, 0x05 }); // mov [rip+offset], rax
            int raxOffset = (int)(sharedMemory.ToInt64() + 16 - (caveAddr.ToInt64() + code.Count + 4));
            code.AddRange(BitConverter.GetBytes(raxOffset));
            
            // Original instruction
            code.AddRange(new byte[] { 0x48, 0x8B, 0xB8, 0x28, 0x02, 0x00, 0x00 }); // mov rdi,[rax+228]
            
            // Save RDI (GameInstance) to shared memory + 8
            code.AddRange(new byte[] { 0x48, 0x89, 0x3D }); // mov [rip+offset], rdi
            int offset = (int)(sharedMemory.ToInt64() + 8 - (caveAddr.ToInt64() + code.Count + 4));
            code.AddRange(BitConverter.GetBytes(offset));
            
            // Jump back
            byte[] jmpBack = CodeInjector.GenerateRelativeJump(
                new IntPtr(caveAddr.ToInt64() + code.Count),
                new IntPtr(hookAddr.ToInt64() + 7)
            );
            if (jmpBack != null)
            {
                code.AddRange(jmpBack);
            }

            if (!memory.WriteBytes(caveAddr, code.ToArray()))
            {
                return false;
            }

            byte[] jmp = CodeInjector.GenerateRelativeJump(hookAddr, caveAddr);
            if (jmp == null)
            {
                // Absolute jump is 14 bytes - too large! Log error and fail
                DebugLogger.Log($"Cave too far from hook (>2GB). Cannot use 7-byte instruction space.", LogLevel.Error);
                return false;
            }
            
            if (jmp.Length < 7)
            {
                byte[] padded = new byte[7];
                Array.Copy(jmp, padded, jmp.Length);
                for (int i = jmp.Length; i < 7; i++)
                {
                    padded[i] = 0x90;
                }
                jmp = padded;
            }

            return injector.CreateDetour(hookAddr, jmp, out _);
        }

        // Read captured pointers
        public bool ReadCapturedPointers(out IntPtr gameState, out IntPtr gameInstance)
        {
            gameState = IntPtr.Zero;
            gameInstance = IntPtr.Zero;

            if (sharedMemory == IntPtr.Zero)
                return false;

            byte[] buffer = new byte[Marshal.SizeOf<CapturedPointers>()];  
            if (!memory.ReadBytes(sharedMemory, buffer.Length, out buffer))
                return false;

            var ptrs = ByteArrayToStructure<CapturedPointers>(buffer);
            
            // If we have the base object pointer, derive missing pointers from it
            if (ptrs.BaseObjectPtr != 0)
            {
                IntPtr basePtr = new IntPtr(ptrs.BaseObjectPtr);
                
                // Derive GameState from base if not directly captured
                if (ptrs.GameStatePtr == 0)
                {
                    IntPtr gsAddr = basePtr + 0x1B0;
                    if (memory.ReadPointer(gsAddr, out IntPtr derivedGS))
                    {
                        if (derivedGS != IntPtr.Zero && derivedGS.ToInt64() > 0x10000)
                        {
                            gameState = derivedGS;
                            DebugLogger.Log($"Derived GameState from base: 0x{derivedGS.ToInt64():X}", LogLevel.Debug);
                        }
                    }
                    else
                    {
                        DebugLogger.Log($"Failed to read GameState from base at 0x{gsAddr.ToInt64():X}", LogLevel.Warning);
                    }
                }
                else
                {
                    gameState = new IntPtr(ptrs.GameStatePtr);
                }
                
                // Derive GameInstance from base if not directly captured
                if (ptrs.GameInstancePtr == 0)
                {
                    IntPtr giAddr = basePtr + 0x228;
                    if (memory.ReadPointer(giAddr, out IntPtr derivedGI))
                    {
                        if (derivedGI != IntPtr.Zero && derivedGI.ToInt64() > 0x10000)
                        {
                            gameInstance = derivedGI;
                            DebugLogger.Log($"Derived GameInstance from base: 0x{derivedGI.ToInt64():X}", LogLevel.Debug);
                        }
                    }
                    else
                    {
                        DebugLogger.Log($"Failed to read GameInstance from base at 0x{giAddr.ToInt64():X}", LogLevel.Warning);
                    }
                }
                else
                {
                    gameInstance = new IntPtr(ptrs.GameInstancePtr);
                }
            }
            else
            {
                // Fallback to direct capture
                if (ptrs.GameStatePtr != 0)
                {
                    gameState = new IntPtr(ptrs.GameStatePtr);
                }
                
                if (ptrs.GameInstancePtr != 0)
                {
                    gameInstance = new IntPtr(ptrs.GameInstancePtr);
                }
            }

            return gameState != IntPtr.Zero || gameInstance != IntPtr.Zero;
        }

        private static T ByteArrayToStructure<T>(byte[] bytes) where T : struct
        {
            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                return (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
            }
            finally
            {
                handle.Free();
            }
        }
    }
}
