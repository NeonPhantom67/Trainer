using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace RVTrainer
{
    internal enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }
    
    internal static class DebugLogger
    {
#if DEBUG
        private static readonly object _logLock = new object();
        private static string _logFilePath;
        private static bool _isInitialized = false;

        static DebugLogger()
        {
            Initialize();
        }

        [Conditional("DEBUG")]
        public static void Initialize()
        {
            if (_isInitialized) return;
            
            try
            {
                string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RVTrainer_Logs");
                Directory.CreateDirectory(logDir);
                
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _logFilePath = Path.Combine(logDir, $"trainer_{timestamp}.log");
                
                _isInitialized = true;
                
                WriteToFile($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] RV Trainer Debug Log Started");
                WriteToFile("=".PadRight(80, '='));
            }
            catch
            {
                _isInitialized = false;
            }
        }

        [Conditional("DEBUG")]
        public static void Log(string message, LogLevel level = LogLevel.Info)
        {
            if (!_isInitialized) return;
            
            string levelStr = level switch
            {
                LogLevel.Debug => "DEBUG",
                LogLevel.Info => "INFO",
                LogLevel.Warning => "WARN",
                LogLevel.Error => "ERROR",
                _ => "INFO"
            };
            
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string logEntry = $"[{timestamp}] [{levelStr,-5}] {message}";
            
            lock (_logLock)
            {
                WriteToFile(logEntry);
                Console.WriteLine(logEntry);
            }
        }

        [Conditional("DEBUG")]
        public static void Warning(string message)
        {
            Log(message, LogLevel.Warning);
        }

        [Conditional("DEBUG")]
        public static void Error(string message, Exception ex = null)
        {
            if (ex != null)
            {
                Log($"{message} - {ex.GetType().Name}: {ex.Message}", LogLevel.Error);
            }
            else
            {
                Log(message, LogLevel.Error);
            }
        }

        [Conditional("DEBUG")]
        public static void LogError(string context, Exception ex)
        {
            Error($"Error in {context}", ex);
        }

        [Conditional("DEBUG")]
        public static void LogCheatToggle(string cheatName, bool enabled)
        {
            string status = enabled ? "ENABLED" : "DISABLED";
            Log($"Cheat '{cheatName}' {status}");
        }

        [Conditional("DEBUG")]
        public static void Debug(string message)
        {
            Log(message, LogLevel.Debug);
        }

        private static void WriteToFile(string content)
        {
            try
            {
                File.AppendAllText(_logFilePath, content + Environment.NewLine);
            }
            catch { }
        }

#else
        // Release build - all methods are empty and will be optimized out
        
        [Conditional("DEBUG")]
        public static void Initialize() { }
        
        [Conditional("DEBUG")]
        public static void Log(string message, LogLevel level = LogLevel.Info) { }
        
        [Conditional("DEBUG")]
        public static void Warning(string message) { }
        
        [Conditional("DEBUG")]
        public static void Error(string message, Exception ex = null) { }
        
        [Conditional("DEBUG")]
        public static void LogError(string context, Exception ex) { }
        
        [Conditional("DEBUG")]
        public static void LogCheatToggle(string cheatName, bool enabled) { }
        
        [Conditional("DEBUG")]
        public static void Debug(string message) { }
#endif
    }
}
