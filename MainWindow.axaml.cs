using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace RVTrainer;

public partial class MainWindow : Window, IDisposable
{
    private MemoryManager? _memoryManager;
    private CheatFunctions? _cheatFunctions;
    private DispatcherTimer? _statusTimer;
    private GlobalHotKeyManager? _hotKeyManager;
    private Win32MessageHook? _messageHook;
    private volatile bool _gameDetected = false;
    private volatile bool _cheatsInitialized = false;
    private volatile bool _isInitializing = false;
    private IntPtr _windowHandle = IntPtr.Zero;
    private readonly object _initLock = new object();

    public MainWindow()
    {
        InitializeComponent();
        DebugLogger.Initialize();
        DebugLogger.Log("Main window initialized");
        
        BuildCheatsUI();
        StartMonitoring();
        
        // Get window handle for global hotkeys
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            this.Opened += (s, e) => 
            {
                Task.Delay(500).ContinueWith(_ => 
                {
                    Dispatcher.UIThread.Post(() => 
                    {
                        var platformHandle = GetWindowHandle();
                        if (platformHandle != null && platformHandle.Handle != IntPtr.Zero)
                        {
                            _windowHandle = platformHandle.Handle;
                            SetupGlobalHotkeys();
                            InstallMessageHook();
                            DebugLogger.Log($"Window handle obtained: 0x{_windowHandle.ToInt64():X}", LogLevel.Info);
                        }
                    });
                });
            };
        }
    }
    
    private IPlatformHandle? GetWindowHandle()
    {
        try
        {
            return this.TryGetPlatformHandle();
        }
        catch (COMException ex)
        {
            DebugLogger.Warning($"COM initialization failed (expected in debug builds): {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            DebugLogger.Warning($"Failed to get window handle: {ex.Message}");
            return null;
        }
    }

    private void SetupGlobalHotkeys()
    {
        _hotKeyManager = new GlobalHotKeyManager();
        
        var hotkeyMap = new Dictionary<string, VirtualKeys>
        {
            { "F1", VirtualKeys.F1 },
            { "F2", VirtualKeys.F2 },
            { "F3", VirtualKeys.F3 },
            { "F4", VirtualKeys.F4 },
            { "F5", VirtualKeys.F5 },
            { "F6", VirtualKeys.F6 },
        };
        
        foreach (var cheat in TrainerConfig.Cheats)
        {
            if (hotkeyMap.TryGetValue(cheat.Hotkey, out var key))
            {
                _hotKeyManager.Register(_windowHandle, key, 0, () => 
                {
                    Dispatcher.UIThread.Post(() => ToggleCheat(cheat.Name));
                });
            }
        }
        
        DebugLogger.Log("Global hotkeys registered", LogLevel.Info);
    }
    
    private void InstallMessageHook()
    {
        _messageHook = new Win32MessageHook();
        _messageHook.InstallHook(this, (hotkeyId) => 
        {
            _hotKeyManager?.HandleHotKey(hotkeyId);
        });
    }
    
    private void ToggleCheat(string cheatName)
    {
        var cheat = TrainerConfig.Cheats.FirstOrDefault(c => c.Name == cheatName);
        if (cheat != null)
        {
            cheat.Enabled = !cheat.Enabled;
            ApplyCheat(cheatName, cheat.Enabled);
            
            // Update UI checkbox
            UpdateCheatCheckbox(cheatName, cheat.Enabled);
        }
    }
    
    private void UpdateCheatCheckbox(string cheatName, bool isChecked)
    {
        var panel = this.FindControl<StackPanel>("CheatsPanel");
        if (panel != null)
        {
            foreach (var border in panel.Children.OfType<Border>())
            {
                if (border.Child is StackPanel categoryStack)
                {
                    foreach (var cheatRow in categoryStack.Children.OfType<StackPanel>())
                    {
                        var checkbox = cheatRow.Children.OfType<CheckBox>().FirstOrDefault();
                        if (checkbox?.Tag as string == cheatName)
                        {
                            checkbox.IsChecked = isChecked;
                            return;
                        }
                    }
                }
            }
        }
    }

    private void BuildCheatsUI()
    {
        var panel = this.FindControl<StackPanel>("CheatsPanel");
        if (panel == null) return;

        var categories = TrainerConfig.Cheats
            .GroupBy(c => c.Category)
            .OrderBy(g => g.Key);

        foreach (var category in categories)
        {
            var categoryBorder = new Border
            {
                Classes = { "category" }
            };

            var categoryStack = new StackPanel();
            
            var categoryTitle = new TextBlock
            {
                Text = $"▸ {category.Key}",
                Classes = { "category-title" }
            };
            categoryStack.Children.Add(categoryTitle);

            foreach (var cheat in category)
            {
                var cheatRow = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Margin = new Thickness(0, 3, 0, 3)
                };
                
                var checkBox = new CheckBox
                {
                    Content = $"{cheat.Name} [{cheat.Hotkey}]",
                    IsChecked = cheat.Enabled,
                    Tag = cheat.Name,
                    Width = 220,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };
                checkBox.IsCheckedChanged += OnCheatToggled;
                cheatRow.Children.Add(checkBox);
                
                // Add value slider if cheat has custom value
                if (cheat.HasCustomValue)
                {
                    var slider = new Slider
                    {
                        Minimum = cheat.MinValue,
                        Maximum = cheat.MaxValue,
                        Value = cheat.CurrentValue,
                        Width = 240,
                        Tag = cheat.Name,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    };
                    slider.ValueChanged += OnSliderValueChanged;
                    
                    var valueBox = new TextBox
                    {
                        Text = cheat.CurrentValue.ToString("F1"),
                        Width = 55,
                        Height = 26,
                        FontSize = 12,
                        TextAlignment = Avalonia.Media.TextAlignment.Center,
                        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Tag = cheat.Name
                    };
                    valueBox.LostFocus += OnValueBoxLostFocus;
                    
                    cheatRow.Children.Add(slider);
                    cheatRow.Children.Add(valueBox);
                }
                
                categoryStack.Children.Add(cheatRow);
            }

            categoryBorder.Child = categoryStack;
            panel.Children.Add(categoryBorder);
        }
    }

    private void StartMonitoring()
    {
        _statusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _statusTimer.Tick += CheckGameStatus;
        _statusTimer.Start();
    }

    private async void CheckGameStatus(object? sender, EventArgs e)
    {
        try
        {
            bool wasDetected = _gameDetected;
            _gameDetected = IsProcessRunning(TrainerConfig.GameProcessName);

            if (_gameDetected && !wasDetected && !_cheatsInitialized && !_isInitializing)
            {
                await InitializeCheats();
            }
            else if (!_gameDetected && wasDetected)
            {
                CleanupCheats();
            }

            UpdateStatusText();
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("CheckGameStatus", ex);
        }
    }

    private bool IsProcessRunning(string processName)
    {
        try
        {
            var processes = Process.GetProcessesByName(processName.Replace(".exe", ""));
            return processes.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task InitializeCheats()
    {
        lock (_initLock)
        {
            if (_isInitializing || _cheatsInitialized)
                return;
            _isInitializing = true;
        }

        try
        {
            DebugLogger.Log("Attempting to initialize cheats...");
            
            _memoryManager = new MemoryManager();
            
            if (_memoryManager.AttachToGame(TrainerConfig.GameProcessName))
            {
                DebugLogger.Log("Successfully attached to game process", LogLevel.Info);
                
                _cheatFunctions = new CheatFunctions(_memoryManager);
                
                if (_cheatFunctions.Initialize())
                {
                    DebugLogger.Log("CheatFunctions initialized successfully", LogLevel.Info);
                    _cheatFunctions.StartUpdateLoop();
                    _cheatsInitialized = true;
                }
                else
                {
                    DebugLogger.Log("Failed to initialize CheatFunctions", LogLevel.Error);
                }
            }
            else
            {
                DebugLogger.Log($"Failed to attach to game: {TrainerConfig.GameProcessName}", LogLevel.Warning);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("InitializeCheats", ex);
            _cheatsInitialized = false;
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private void CleanupCheats()
    {
        try
        {
            _cheatFunctions?.StopUpdateLoop();
            _cheatFunctions?.Dispose();
            _cheatFunctions = null;
            _memoryManager?.Detach();
            _memoryManager?.Dispose();
            _memoryManager = null;
            _cheatsInitialized = false;
            
            // Reset all checkboxes
            var panel = this.FindControl<StackPanel>("CheatsPanel");
            if (panel != null)
            {
                foreach (var border in panel.Children.OfType<Border>())
                {
                    if (border.Child is StackPanel stack)
                    {
                        foreach (var checkbox in stack.Children.OfType<CheckBox>())
                        {
                            checkbox.IsChecked = false;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("CleanupCheats", ex);
        }
    }

    private void UpdateStatusText()
    {
        var statusText = this.FindControl<TextBlock>("StatusText");
        if (statusText == null) return;

        if (_gameDetected && _cheatsInitialized)
        {
            bool hasGameState = _cheatFunctions?.HasGameState() ?? false;
            if (hasGameState)
            {
                statusText.Text = "Game detected! All cheats ready.";
                statusText.Foreground = new SolidColorBrush(Color.Parse("#3fb950"));
            }
            else
            {
                statusText.Text = "Item cheats unavailable. Use items in-game first.";
                statusText.Foreground = new SolidColorBrush(Color.Parse("#d29922"));
            }
        }
        else if (_gameDetected)
        {
            statusText.Text = "Game detected but initialization failed";
            statusText.Foreground = new SolidColorBrush(Color.Parse("#f85149"));
        }
        else
        {
            statusText.Text = "Waiting for game...";
            statusText.Foreground = new SolidColorBrush(Color.Parse("#7d8590"));
        }
    }

    private void OnCheatToggled(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox || checkBox.Tag is not string cheatName)
            return;

        bool enabled = checkBox.IsChecked ?? false;
        ApplyCheat(cheatName, enabled);
        
        // Update config
        var cheat = TrainerConfig.Cheats.FirstOrDefault(c => c.Name == cheatName);
        if (cheat != null)
        {
            cheat.Enabled = enabled;
        }
    }

    private void OnSliderValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is Slider slider && slider.Tag is string cheatName)
        {
            var cheat = TrainerConfig.Cheats.FirstOrDefault(c => c.Name == cheatName);
            if (cheat != null)
            {
                cheat.CurrentValue = slider.Value;
                
                // Update text box
                UpdateValueTextBox(cheatName, slider.Value);
                
                // Re-apply cheat if it's enabled
                if (cheat.Enabled)
                {
                    ApplyCheat(cheatName, true);
                }
            }
        }
    }
    
    private void OnValueBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.Tag is string cheatName)
        {
            var cheat = TrainerConfig.Cheats.FirstOrDefault(c => c.Name == cheatName);
            if (cheat != null && double.TryParse(textBox.Text, out double value))
            {
                value = Math.Clamp(value, cheat.MinValue, cheat.MaxValue);
                cheat.CurrentValue = value;
                textBox.Text = value.ToString("F1");
                
                // Update slider
                UpdateValueSlider(cheatName, value);
                
                // Re-apply cheat if it's enabled
                if (cheat.Enabled)
                {
                    ApplyCheat(cheatName, true);
                }
            }
        }
    }
    
    private void UpdateValueTextBox(string cheatName, double value)
    {
        var panel = this.FindControl<StackPanel>("CheatsPanel");
        if (panel != null)
        {
            foreach (var border in panel.Children.OfType<Border>())
            {
                if (border.Child is StackPanel categoryStack)
                {
                    foreach (var cheatRow in categoryStack.Children.OfType<StackPanel>())
                    {
                        var textBox = cheatRow.Children.OfType<TextBox>().FirstOrDefault();
                        if (textBox?.Tag as string == cheatName)
                        {
                            textBox.Text = value.ToString("F1");
                            return;
                        }
                    }
                }
            }
        }
    }
    
    private void UpdateValueSlider(string cheatName, double value)
    {
        var panel = this.FindControl<StackPanel>("CheatsPanel");
        if (panel != null)
        {
            foreach (var border in panel.Children.OfType<Border>())
            {
                if (border.Child is StackPanel categoryStack)
                {
                    foreach (var cheatRow in categoryStack.Children.OfType<StackPanel>())
                    {
                        var slider = cheatRow.Children.OfType<Slider>().FirstOrDefault();
                        if (slider?.Tag as string == cheatName)
                        {
                            slider.Value = value;
                            return;
                        }
                    }
                }
            }
        }
    }

    private void ApplyCheat(string cheatName, bool enabled)
    {
        if (!_cheatsInitialized || _cheatFunctions == null)
            return;

        try
        {
            var cheat = TrainerConfig.Cheats.FirstOrDefault(c => c.Name == cheatName);
            if (cheat == null) return;
            
            switch (cheatName)
            {
                case "Fly Hack (Noclip)":
                    _cheatFunctions.FlyHack(enabled);
                    break;
                case "Speed Hack":
                    _cheatFunctions.SpeedHack(enabled, (float)cheat.CurrentValue);
                    break;
                case "Super Jump":
                    _cheatFunctions.SuperJump(enabled, (float)cheat.CurrentValue);
                    break;
                case "Inf EpiPen":
                    _cheatFunctions.InfEpiPen(enabled, (int)cheat.CurrentValue);
                    break;
                case "Inf Parts":
                    _cheatFunctions.InfParts(enabled, (int)cheat.CurrentValue);
                    break;
                case "Super Strength":
                    _cheatFunctions.SuperStrength(enabled, cheat.CurrentValue);
                    break;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError($"ApplyCheat({cheatName})", ex);
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        Dispose();
        base.OnClosing(e);
    }

    public void Dispose()
    {
        _statusTimer?.Stop();
        _statusTimer = null;
        _hotKeyManager?.UnregisterAll();
        _hotKeyManager = null;
        CleanupCheats();
        DebugLogger.Log("Application closing");
    }
}
