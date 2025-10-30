using System;
using System.Linq;
using System.Threading;

namespace RVTrainer
{
    internal class CheatFunctions : IDisposable
    {
        private readonly MemoryManager memory;
        private volatile IntPtr gameStatePointer = IntPtr.Zero;
        private volatile IntPtr gameInstancePointer = IntPtr.Zero;
        private Thread? updateThread;
        private volatile bool isRunning = false;
        private CodeInjector? injector;
        private PointerCapture? capture;
        
        // Memory offsets for resource hacks
        private const int OFFSET_EPIPEN = 0x348;
        private const int OFFSET_PARTS = 0x34C;
        
        // Active cheat states
        private volatile bool flyHackActive = false;
        private volatile bool speedHackActive = false;
        private volatile bool superJumpActive = false;
        private volatile bool infEpipenActive = false;
        private volatile bool infPartsActive = false;
        private volatile bool superStrengthActive = false;

        public CheatFunctions(MemoryManager memoryManager)
        {
            memory = memoryManager;
            DebugLogger.Log("CheatFunctions initialized", LogLevel.Info);
        }

        public bool HasGameState()
        {
            return gameStatePointer != IntPtr.Zero;
        }



        public bool Initialize()
        {
            DebugLogger.Log("Initializing CheatFunctions...", LogLevel.Info);
            
            if (!memory.IsAttached)
            {
                DebugLogger.Log("Memory not attached", LogLevel.Error);
                return false;
            }

            // Find patterns for code injection 
            // Pattern: 48 8B 88 B0 01 00 00 48 8B 01 FF
            IntPtr gstatePattern = memory.FindPattern("488B88B0010000488B01FF"); // mov rcx,[rax+1B0]; mov rax,[rcx]; (call/jmp)
            IntPtr ginstancePattern = memory.FindPattern("488BB828020000"); // mov rdi,[rax+228]
            
            if (gstatePattern != IntPtr.Zero)
            {
                DebugLogger.Log($"✓ GameState pattern found at 0x{gstatePattern.ToInt64():X}", LogLevel.Info);
            }
            
            if (ginstancePattern != IntPtr.Zero)
            {
                DebugLogger.Log($"✓ GameInstance pattern found at 0x{ginstancePattern.ToInt64():X}", LogLevel.Info);
            }
            
            if (gstatePattern == IntPtr.Zero && ginstancePattern == IntPtr.Zero)
            {
                DebugLogger.Log("No patterns found for code injection", LogLevel.Error);
                return false;
            }
            
            // Initialize injection system
            injector = new CodeInjector(memory.ProcessHandle, memory);
            capture = new PointerCapture(memory.ProcessHandle, memory, injector);
            
            // Setup pointer capture hooks
            if (!capture.SetupCapture(gstatePattern, ginstancePattern))
            {
                DebugLogger.Log("Failed to setup pointer capture", LogLevel.Error);
                return false;
            }
            
            DebugLogger.Log("✓ Code injection successful! Waiting for pointers...", LogLevel.Info);
            
            // Wait for at least one pointer to be captured (max 10 seconds)
            for (int i = 0; i < 10; i++)
            {
                Thread.Sleep(1000);
                
                if (capture.ReadCapturedPointers(out IntPtr capturedGS, out IntPtr capturedGI))
                {
                    if (capturedGS != IntPtr.Zero && capturedGS.ToInt64() > 0x10000 && gameStatePointer == IntPtr.Zero)
                    {
                        gameStatePointer = capturedGS;
                        DebugLogger.Log($"✓ Captured GameState: 0x{gameStatePointer.ToInt64():X}", LogLevel.Info);
                    }
                    
                    if (capturedGI != IntPtr.Zero && capturedGI.ToInt64() > 0x10000 && gameInstancePointer == IntPtr.Zero)
                    {
                        gameInstancePointer = capturedGI;
                        DebugLogger.Log($"✓ Captured GameInstance: 0x{gameInstancePointer.ToInt64():X}", LogLevel.Info);
                        

                    }
                    
                    // Break if we have at least one pointer
                    if (gameStatePointer != IntPtr.Zero || gameInstancePointer != IntPtr.Zero)
                    {
                        DebugLogger.Log($"✓ Pointer capture successful after {i + 1} seconds", LogLevel.Info);
                        break;
                    }
                }
            }
            
            if (gameStatePointer == IntPtr.Zero && gameInstancePointer == IntPtr.Zero)
            {
                DebugLogger.Log("Failed to capture any game pointers", LogLevel.Error);
                return false;
            }
            
            // Log which pointers were captured
            if (gameStatePointer == IntPtr.Zero)
            {
                DebugLogger.Log("⚠ GameState not captured yet - interact with items in-game to enable EpiPen/Parts cheats", LogLevel.Warning);
            }
            else
            {
                DebugLogger.Log("✓ GameState captured - EpiPen/Parts cheats ready", LogLevel.Info);
            }
            
            if (gameInstancePointer == IntPtr.Zero)
            {
                DebugLogger.Log("⚠ GameInstance not captured yet - move in-game to enable movement cheats", LogLevel.Warning);
            }
            else
            {
                DebugLogger.Log("✓ GameInstance captured - Movement cheats ready", LogLevel.Info);
            }
            
            DebugLogger.Log("Initialization complete", LogLevel.Info);
            return true;
        }

        public bool FlyHack(bool enable)
        {
            flyHackActive = enable;
            DebugLogger.LogCheatToggle("Fly Hack", enable);
            
            if (gameInstancePointer == IntPtr.Zero) return false;
            
            IntPtr movementModeAddr = memory.GetAddressFromPointerChain(
                gameInstancePointer, 0x38, 0x0, 0x30, 0x2F8, 0x330, 0x231
            );
            
            if (movementModeAddr == IntPtr.Zero) return false;
            
            byte modeValue = (byte)(enable ? 5 : 1);
            return memory.WriteBytes(movementModeAddr, new byte[] { modeValue });
        }

        public bool SpeedHack(bool enable, float multiplier = 2.0f)
        {
            speedHackActive = enable;
            DebugLogger.LogCheatToggle("Speed Hack", enable);
            
            if (gameInstancePointer == IntPtr.Zero)
            {
                return false;
            }
            
            IntPtr speedAddr = memory.GetAddressFromPointerChain(
                gameInstancePointer, 0x38, 0x0, 0x30, 0x2F8, 0x330, 0x278
            );
            
            if (speedAddr == IntPtr.Zero)
            {
                return false;
            }
            
            float baseSpeed = 600.0f;
            float value = enable ? baseSpeed * multiplier : baseSpeed;
            return memory.WriteFloat(speedAddr, value);
        }

        public bool SuperJump(bool enable, float height = 1000.0f)
        {
            superJumpActive = enable;
            DebugLogger.LogCheatToggle("Super Jump", enable);
            
            if (gameInstancePointer == IntPtr.Zero) return false;
            
            IntPtr jumpAddr = memory.GetAddressFromPointerChain(
                gameInstancePointer, 0x38, 0x0, 0x30, 0x2F8, 0x330, 0x1A8
            );
            
            if (jumpAddr == IntPtr.Zero) return false;
            
            float value = enable ? height : 420.0f;
            return memory.WriteFloat(jumpAddr, value);
        }

        public bool InfEpiPen(bool enable, int count = 999)
        {
            infEpipenActive = enable;
            DebugLogger.LogCheatToggle("Inf EpiPen", enable);
            
            if (gameStatePointer == IntPtr.Zero)
            {
                return false;
            }
            
            IntPtr epipenAddr = gameStatePointer + OFFSET_EPIPEN;
            int value = enable ? count : 0;
            return memory.WriteInt32(epipenAddr, value);
        }

        public bool InfParts(bool enable, int count = 999)
        {
            infPartsActive = enable;
            DebugLogger.LogCheatToggle("Inf Parts", enable);
            
            if (gameStatePointer == IntPtr.Zero)
            {
                return false;
            }
            
            IntPtr partsAddr = gameStatePointer + OFFSET_PARTS;
            int value = enable ? count : 0;
            return memory.WriteInt32(partsAddr, value);
        }

        public bool SuperStrength(bool enable, double multiplier = 10.0)
        {
            superStrengthActive = enable;
            DebugLogger.LogCheatToggle("Super Strength", enable);
            
            if (gameInstancePointer == IntPtr.Zero) return false;
            
            IntPtr strengthAddr = memory.GetAddressFromPointerChain(
                gameInstancePointer, 0x38, 0x0, 0x30, 0x2F8, 0xC18
            );
            
            if (strengthAddr == IntPtr.Zero) return false;
            
            double value = enable ? multiplier : 1.0;
            return memory.WriteDouble(strengthAddr, value);
        }

        public void StartUpdateLoop()
        {
            if (isRunning) return;
            
            isRunning = true;
            updateThread = new Thread(UpdateLoop) { IsBackground = true };
            updateThread.Start();
            DebugLogger.Log("Update loop started", LogLevel.Info);
        }

        public void StopUpdateLoop()
        {
            isRunning = false;
            updateThread?.Join(1000);
            DebugLogger.Log("Update loop stopped", LogLevel.Info);
        }

        public void Dispose()
        {
            StopUpdateLoop();
            injector = null;
            capture = null;
        }

        private void UpdateLoop()
        {
            int counter = 0;
            while (isRunning)
            {
                try
                {
                    if (!memory.IsAttached)
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    // Every 15 seconds, retry pointer capture if missing either one
                    counter = (counter + 1) % 150;
                    if (counter == 0 && capture != null && (gameStatePointer == IntPtr.Zero || gameInstancePointer == IntPtr.Zero))
                    {
                        DebugLogger.Log($"Retrying pointer capture (GS: {(gameStatePointer != IntPtr.Zero ? "✓" : "✗")}, GI: {(gameInstancePointer != IntPtr.Zero ? "✓" : "✗")})", LogLevel.Info);
                        
                        if (capture.ReadCapturedPointers(out IntPtr gs, out IntPtr gi))
                        {
                            if (gs != IntPtr.Zero && gs.ToInt64() > 0x10000 && gameStatePointer == IntPtr.Zero)
                            {
                                gameStatePointer = gs;
                                DebugLogger.Log($"✓ GameState captured: 0x{gs.ToInt64():X} - EpiPen/Parts now available!", LogLevel.Info);
                            }
                            
                            if (gi != IntPtr.Zero && gi.ToInt64() > 0x10000 && gameInstancePointer == IntPtr.Zero)
                            {
                                gameInstancePointer = gi;
                                DebugLogger.Log($"✓ GameInstance captured: 0x{gi.ToInt64():X} - Movement cheats now available!", LogLevel.Info);
                            }
                            
                            if (gameStatePointer != IntPtr.Zero && gameInstancePointer != IntPtr.Zero)
                            {
                                DebugLogger.Log("✓ All pointers captured - all cheats ready!", LogLevel.Info);
                            }
                        }
                        
                    }

                    // Maintain active cheats with error handling
                    if (flyHackActive && gameInstancePointer != IntPtr.Zero)
                    {
                        try
                        {
                            IntPtr addr = memory.GetAddressFromPointerChain(
                                gameInstancePointer, 0x38, 0x0, 0x30, 0x2F8, 0x330, 0x231
                            );
                            if (addr != IntPtr.Zero)
                            {
                                if (!memory.WriteBytes(addr, new byte[] { 5 }))
                                {
                                    DebugLogger.Debug("Failed to write fly hack value");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.Debug($"Fly hack error: {ex.Message}");
                        }
                    }
                    
                    // Maintain speed hack
                    if (speedHackActive && gameInstancePointer != IntPtr.Zero)
                    {
                        try
                        {
                            IntPtr addr = memory.GetAddressFromPointerChain(
                                gameInstancePointer, 0x38, 0x0, 0x30, 0x2F8, 0x330, 0x278
                            );
                            if (addr != IntPtr.Zero)
                            {
                                var cheat = TrainerConfig.Cheats.FirstOrDefault(c => c.Name == "Speed Hack");
                                float multiplier = cheat != null ? (float)cheat.CurrentValue : 2.0f;
                                float value = 600.0f * multiplier;
                                if (!memory.WriteFloat(addr, value))
                                {
                                    DebugLogger.Debug("Failed to write speed hack value");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.Debug($"Speed hack error: {ex.Message}");
                        }
                    }
                    
                    // Maintain epipen/parts - use direct offset since gameStatePointer is the base
                    if (infEpipenActive && gameStatePointer != IntPtr.Zero)
                    {
                        IntPtr addr = gameStatePointer + OFFSET_EPIPEN;
                        var cheat = TrainerConfig.Cheats.FirstOrDefault(c => c.Name == "Inf EpiPen");
                        int value = cheat != null ? (int)cheat.CurrentValue : 999;
                        memory.WriteInt32(addr, value);
                    }
                    
                    if (infPartsActive && gameStatePointer != IntPtr.Zero)
                    {
                        IntPtr addr = gameStatePointer + OFFSET_PARTS;
                        var cheat = TrainerConfig.Cheats.FirstOrDefault(c => c.Name == "Inf Parts");
                        int value = cheat != null ? (int)cheat.CurrentValue : 999;
                        memory.WriteInt32(addr, value);
                    }
                    
                    // Continue capturing pointers if not yet found
                    if (capture != null && (gameStatePointer == IntPtr.Zero || gameInstancePointer == IntPtr.Zero))
                    {
                        if (capture.ReadCapturedPointers(out IntPtr gs, out IntPtr gi))
                        {
                            bool newPointerCaptured = false;
                            
                            if (gs != IntPtr.Zero && gs.ToInt64() > 0x10000 && gameStatePointer == IntPtr.Zero)
                            {
                                gameStatePointer = gs;
                                DebugLogger.Log($"✓ GameState captured in background: 0x{gs.ToInt64():X} - EpiPen/Parts cheats now available!", LogLevel.Info);
                                newPointerCaptured = true;
                                
                                // Re-apply item cheats if they were enabled before capture
                                if (infEpipenActive)
                                {
                                    var cheat = TrainerConfig.Cheats.FirstOrDefault(c => c.Name == "Inf EpiPen");
                                    InfEpiPen(true, cheat != null ? (int)cheat.CurrentValue : 999);
                                }
                                if (infPartsActive)
                                {
                                    var cheat = TrainerConfig.Cheats.FirstOrDefault(c => c.Name == "Inf Parts");
                                    InfParts(true, cheat != null ? (int)cheat.CurrentValue : 999);
                                }
                            }
                            else if (gs != IntPtr.Zero && gameStatePointer == IntPtr.Zero)
                            {
                                // Hook is firing but pointer is invalid
                                DebugLogger.Debug($"GameState hook fired but pointer invalid: 0x{gs.ToInt64():X}");
                            }
                            
                            if (gi != IntPtr.Zero && gi.ToInt64() > 0x10000 && gameInstancePointer == IntPtr.Zero)
                            {
                                gameInstancePointer = gi;
                                DebugLogger.Log($"✓ GameInstance captured in background: 0x{gi.ToInt64():X} - Movement cheats now available!", LogLevel.Info);
                                newPointerCaptured = true;
                            }
                            
                            // Stop checking once both are captured
                            if (newPointerCaptured && gameStatePointer != IntPtr.Zero && gameInstancePointer != IntPtr.Zero)
                            {
                                DebugLogger.Log("✓ All pointers captured - all cheats ready!", LogLevel.Info);
                            }
                        }
                    }

                    Thread.Sleep(100);
                }
                catch (Exception ex)
                {
                    DebugLogger.LogError("UpdateLoop", ex);
                    Thread.Sleep(100);
                }
            }
        }
    }
}
