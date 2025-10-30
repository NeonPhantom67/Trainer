using System.Collections.Generic;

namespace RVTrainer
{

    internal static class TrainerConfig
    {
 

        public const string TrainerTitle = "RV: There Yet? Trainer v1.0";
        public const string GameVersion = "1.0.0";
        public const string TrainerVersion = "v1.0";
        public const string Author = "NeonPhantom";

        public const string GameProcessName = "Ride-Win64-Shipping.exe";

        public const string ErrorMessage = "Can't open game memory.\nCheck:\n1. Game is running?\n2. Antivirus is off?\n3. Did you tried to run it with admin rights";

        public const string ErrorTitle = "Failure when tried to open game memory";
        
        public const string DebugLogFile = "trainer_debug.log";
        public const bool EnableDebugLogging = true;

        public static readonly List<CheatDefinition> Cheats = new List<CheatDefinition>
        {
            new CheatDefinition { Name = "Fly Hack (Noclip)", Hotkey = "F1", Enabled = false, Category = "Movement" },
            new CheatDefinition { Name = "Speed Hack", Hotkey = "F2", Enabled = false, Category = "Movement", HasCustomValue = true, DefaultValue = 2.0, MinValue = 1.0, MaxValue = 10.0, CurrentValue = 2.0 },
            new CheatDefinition { Name = "Super Jump", Hotkey = "F3", Enabled = false, Category = "Movement", HasCustomValue = true, DefaultValue = 1000.0, MinValue = 420.0, MaxValue = 5000.0, CurrentValue = 1000.0 },
            new CheatDefinition { Name = "Inf EpiPen", Hotkey = "F4", Enabled = false, Category = "Items", HasCustomValue = true, DefaultValue = 999, MinValue = 1, MaxValue = 9999, CurrentValue = 999 },
            new CheatDefinition { Name = "Inf Parts", Hotkey = "F5", Enabled = false, Category = "Items", HasCustomValue = true, DefaultValue = 999, MinValue = 1, MaxValue = 9999, CurrentValue = 999 },
            new CheatDefinition { Name = "Super Strength", Hotkey = "F6", Enabled = false, Category = "Player", HasCustomValue = true, DefaultValue = 10.0, MinValue = 1.0, MaxValue = 100.0, CurrentValue = 10.0 },
        };
    }

    internal class CheatDefinition
    {
        public string Name { get; set; }
        public string Hotkey { get; set; }
        public bool Enabled { get; set; }
        public string Category { get; set; }
        public double DefaultValue { get; set; } = 1.0;
        public double MinValue { get; set; } = 0.0;
        public double MaxValue { get; set; } = 100.0;
        public double CurrentValue { get; set; } = 1.0;
        public bool HasCustomValue { get; set; } = false;
    }
}
