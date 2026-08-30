using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using OsageLagtrain.App.Cycles;
#if WINDOWS
using Windows.Storage.Pickers;
using WinRT.Interop;
#endif

namespace OsageLagtrain.App.Ui;

public sealed partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;
    private readonly DispatcherTimer _previewTimer;
    private bool _isSliderDragging;
    private bool _isInitializing = true;

    public SettingsWindow() : this(CreateDefaultViewModel()) { }

    public SettingsWindow(SettingsViewModel vm)
    {
        // If default VM was created without hwnd provider (e.g. designer), patch picker to use this window's HWND
        if (vm != null)
        {
            try
            {
                var field = typeof(SettingsViewModel).GetField("_filePicker", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var cur = field?.GetValue(vm) as IFilePicker;
                if (cur is WinUIFolderPicker wp && wp.IsZeroHandle)
                {
                    field!.SetValue(vm, new WinUIFolderPicker(() => { try { return WindowNative.GetWindowHandle(this); } catch { return IntPtr.Zero; } }));
                }
            }
            catch { }
        }
        try
        {
            InitializeComponent();
            Console.WriteLine("[SettingsWindow] InitializeComponent ok");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SettingsWindow] InitializeComponent failed: {ex}");
            System.Diagnostics.Debug.WriteLine($"[SettingsWindow] InitializeComponent failed: {ex}");
            try
            {
                Content = new Microsoft.UI.Xaml.Controls.TextBlock
                {
                    Text = $"Settings XAML failed:\n{ex.Message}\n{ex}",
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                    Margin = new Microsoft.UI.Xaml.Thickness(16)
                };
            }
            catch (Exception ex2) { Console.WriteLine($"[SettingsWindow] fallback content failed: {ex2}"); }
            // Continue so Activate still shows something
        }
        _vm = vm;
        // Set NumberBox values in code-behind after InitializeComponent to avoid XAML parse failure (Value assignment via XAML can throw XamlParseException with Minimum validation)
        try
        {
            if (FpsBox != null) FpsBox.Value = 12;
            if (HoldLastBox != null) HoldLastBox.Value = 0;
            // SceneDelayBox left as NaN to show PlaceholderText="global" (per-scene override); do not set Value here
            if (GlobalDelayBox != null) GlobalDelayBox.Value = 500;
            if (NoRepeatBox != null) NoRepeatBox.Value = 3;
        }
        catch (Exception ex) { Console.WriteLine($"[SettingsWindow] NumberBox init failed: {ex.Message}"); }
        _isInitializing = false;
        _previewTimer = new DispatcherTimer();
        _previewTimer.Tick += OnPreviewTick;
        this.Activated += OnActivated;
        this.Closed += (_, _) => { try { _previewTimer.Stop(); } catch { } try { _vm.Dispose(); } catch { } Console.WriteLine("[SettingsWindow] Closed"); };
    }

    private static SettingsViewModel CreateDefaultViewModel(Func<IntPtr>? hwndProvider = null)
    {
        var settingsStore = new SettingsStore();
        var cfg = settingsStore.Load();
        var cycleStore = new CycleStoreAdapter(cfg.CyclesRoot);
        Action<SettingsConfig>? update = null;
        try
        {
            var monitor = new WindowMonitor.WindowMonitor(globalPostEventDelayMs: cfg.PostEventDelayMs);
            update = c => monitor.UpdateConfig(c);
        }
        catch { }
        Func<IntPtr> provider = hwndProvider ?? new Func<IntPtr>(() => IntPtr.Zero);
        var picker = new WinUIFolderPicker(provider);
        return new SettingsViewModel(cycleStore, settingsStore, picker, update, debounceMs: 500);
    }

    private bool _activated;
    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_activated) return;
        _activated = true;
        // Refresh FolderPicker HWND now that window handle is valid
        try
        {
            var field = typeof(SettingsViewModel).GetField("_filePicker", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field?.GetValue(_vm) is WinUIFolderPicker wp && wp.IsZeroHandle)
                field.SetValue(_vm, new WinUIFolderPicker(() => { try { return WindowNative.GetWindowHandle(this); } catch { return IntPtr.Zero; } }));
        }
        catch { }
        await OnLoadedAsync();
    }

    private async Task OnLoadedAsync()
    {
        Console.WriteLine("[SettingsWindow] OnLoadedAsync start");
        _isInitializing = true;
        try
        {
            if (CyclesRootTextBox != null) CyclesRootTextBox.Text = _vm.GlobalSettings.CyclesRoot;
            if (GlobalDelayBox != null) GlobalDelayBox.Value = _vm.GlobalSettings.PostEventDelayMs;
            if (NoRepeatBox != null) NoRepeatBox.Value = _vm.GlobalSettings.NoRepeatWindow;
            if (IdleColorBox != null) IdleColorBox.Text = _vm.GlobalSettings.IdleColor;
            if (SelectionPolicyCombo != null)
            {
                for (int i = 0; i < SelectionPolicyCombo.Items.Count; i++)
                {
                    if (SelectionPolicyCombo.Items[i] is Microsoft.UI.Xaml.Controls.ComboBoxItem ci && (ci.Tag as string) == _vm.GlobalSettings.SelectionPolicy)
                    {
                        SelectionPolicyCombo.SelectedIndex = i; break;
                    }
                }
            }
            if (_vm.HasAppMap && AppMapPanel != null && AppMapText != null)
            {
                AppMapPanel.Visibility = Visibility.Visible;
                AppMapText.Text = string.Join("\n", _vm.AppMap!.Select(kv => $"{kv.Key}: {string.Join(", ", kv.Value)}"));
            }
            Console.WriteLine("[SettingsWindow] OnLoadedAsync initial fields set");
        }
        catch (Exception ex) { Console.WriteLine($"[SettingsWindow] OnLoadedAsync init failed: {ex}"); }
        finally { _isInitializing = false; }

        try { _vm.PropertyChanged += OnVmPropertyChanged; } catch { }
        try
        {
            Console.WriteLine("[SettingsWindow] LoadScenesAsync start");
            await _vm.LoadScenesAsync();
            Console.WriteLine($"[SettingsWindow] LoadScenesAsync done Scenes={_vm.Scenes.Count} selected={_vm.SelectedScene?.Id ?? "null"}");
        }
        catch (Exception ex) { Console.WriteLine($"[SettingsWindow] LoadScenesAsync failed: {ex}"); }
        try
        {
            if (SceneListView != null) SceneListView.ItemsSource = _vm.Scenes;
            if (LoadingRing != null) { LoadingRing.IsActive = false; LoadingRing.Visibility = Visibility.Collapsed; }
            if (_vm.SelectedScene != null && SceneListView != null)
                SceneListView.SelectedItem = _vm.SelectedScene;
            UpdatePreviewImage();
            UpdatePreviewInterval();
            Console.WriteLine("[SettingsWindow] OnLoadedAsync complete");
        }
        catch (Exception ex) { Console.WriteLine($"[SettingsWindow] OnLoadedAsync tail failed: {ex}"); }
    }

    private void OnVmPropertyChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.IsLoading))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                LoadingRing.IsActive = _vm.IsLoading;
                LoadingRing.Visibility = _vm.IsLoading ? Visibility.Visible : Visibility.Collapsed;
            });
        }
        if (e.PropertyName == nameof(SettingsViewModel.CurrentPreviewFramePath) || e.PropertyName == nameof(SettingsViewModel.CurrentFrameIndex))
        {
            DispatcherQueue.TryEnqueue(UpdatePreviewImage);
        }
    }

    private void OnSceneSelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        _isInitializing = true;
        try
        {
            if (SceneListView?.SelectedItem is SceneListItem item)
            {
                _vm.SelectedScene = item;
                if (FpsBox != null) FpsBox.Value = item.Fps;
                if (HoldLastBox != null) HoldLastBox.Value = item.Config?.HoldLastMs ?? 0;
                if (SceneDelayBox != null) SceneDelayBox.Value = item.Config?.PostEventDelayMs ?? double.NaN;
                if (PreviewSlider != null) { PreviewSlider.Maximum = Math.Max(0, item.Frames.Count - 1); PreviewSlider.Value = _vm.CurrentFrameIndex; }
                if (LoopCheckBox != null)
                {
                    string mode = (item.Config?.Mode as OsageLagtrain.App.Cycles.SceneMode.StringMode)?.Value ?? "once";
                    LoopCheckBox.IsChecked = mode == "loop";
                    LoopCheckBox.Content = mode == "loop" ? "Loop ✓" : "Once";
                }
                if (PreviewFrameInfo != null) PreviewFrameInfo.Text = $"{_vm.CurrentFrameIndex + 1} / {item.Frames.Count}  fps {item.Fps}";
                Console.WriteLine($"[SettingsWindow] SelectedScene {item.Id} frames={item.Frames.Count} fps={item.Fps} first={item.Frames.FirstOrDefault()}");
                UpdatePreviewImage();
                UpdatePreviewInterval();
            }
        }
        catch (Exception ex) { Console.WriteLine($"[SettingsWindow] OnSceneSelectionChanged failed: {ex.Message}"); }
        finally { _isInitializing = false; }
    }

    private void UpdatePreviewImage()
    {
        try
        {
            var path = _vm.CurrentPreviewFramePath;
            if (PreviewImage == null) return;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Console.WriteLine($"[SettingsWindow] UpdatePreviewImage no path exists path='{path ?? "null"}' selected={_vm.SelectedScene?.Id} frames={_vm.SelectedScene?.Frames.Count}");
                PreviewImage.Source = null;
                if (PreviewFrameInfo != null) PreviewFrameInfo.Text = _vm.SelectedScene == null ? "No scene selected" : $"No frames ({_vm.SelectedScene.Frames.Count})";
                return;
            }
            try
            {
                // WinUI3 requires file:/// absolute Uri; BitmapImage(path) with raw C:\ may fail silently.
                string fileUrl = path;
                if (!fileUrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                {
                    fileUrl = new Uri(Path.GetFullPath(path), UriKind.Absolute).AbsoluteUri;
                }
                var bmp = new BitmapImage(new Uri(fileUrl, UriKind.Absolute));
                PreviewImage.Source = bmp;
                if (PreviewSlider != null) { PreviewSlider.Maximum = Math.Max(0, (_vm.SelectedScene?.Frames.Count ?? 1) - 1); PreviewSlider.Value = _vm.CurrentFrameIndex; }
                if (PreviewFrameInfo != null && _vm.SelectedScene != null)
                    PreviewFrameInfo.Text = $"{_vm.CurrentFrameIndex + 1} / {_vm.SelectedScene.Frames.Count}  fps {_vm.SelectedScene.Fps}  {( _vm.IsPreviewPlaying ? "▶" : "⏸")}  {Path.GetFileName(path)}";
                Console.WriteLine($"[SettingsWindow] UpdatePreviewImage ok idx={_vm.CurrentFrameIndex} path={Path.GetFileName(path)} fps={_vm.SelectedScene?.Fps}");
            }
            catch (Exception ex) { Console.WriteLine($"[SettingsWindow] UpdatePreviewImage failed: {ex.Message} path={path}"); }
        }
        catch (Exception ex) { Console.WriteLine($"[SettingsWindow] UpdatePreviewImage outer failed: {ex.Message}"); }
    }

    private void UpdatePreviewInterval()
    {
        int fps = _vm.SelectedScene?.Fps ?? 12;
        var interval = Rendering.FrameScheduler.GetInterval(fps);
        _previewTimer.Interval = interval;
        if (_vm.IsPreviewPlaying) _previewTimer.Start(); else _previewTimer.Stop();
    }

    private void OnPreviewTick(object? sender, object e)
    {
        _vm.TickPreview();
    }

    private void OnPlayClicked(object sender, RoutedEventArgs e)
    {
        _vm.PlayPreview();
        _previewTimer.Start();
    }

    private void OnPauseClicked(object sender, RoutedEventArgs e)
    {
        _vm.PausePreview();
        _previewTimer.Stop();
    }

    private void OnLoopToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        bool isLoop = LoopCheckBox?.IsChecked == true;
        _vm.UpdateSelectedMode(isLoop);
        if (LoopCheckBox != null) LoopCheckBox.Content = isLoop ? "Loop ✓" : "Once";
        UpdatePreviewInterval();
    }

    private void OnSliderChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isSliderDragging) return;
        _isSliderDragging = true;
        _vm.ScrubTo((int)e.NewValue);
        UpdatePreviewImage();
        _isSliderDragging = false;
    }

    private void OnFpsChanged(Microsoft.UI.Xaml.Controls.NumberBox sender, Microsoft.UI.Xaml.Controls.NumberBoxValueChangedEventArgs args)
    {
        if (_isInitializing) return;
        if (double.IsNaN(args.NewValue)) return;
        int fps = (int)args.NewValue;
        fps = Math.Clamp(fps, 1, 30);
        if (_vm.SelectedScene != null && _vm.SelectedScene.Fps == fps) return;
        _vm.SelectedFps = fps;
        UpdatePreviewInterval();
    }

    private void OnHoldLastChanged(Microsoft.UI.Xaml.Controls.NumberBox sender, Microsoft.UI.Xaml.Controls.NumberBoxValueChangedEventArgs args)
    {
        if (_isInitializing) return;
        if (double.IsNaN(args.NewValue)) return;
        _vm.SelectedHoldLastMs = (int)args.NewValue;
    }

    private void OnSceneDelayChanged(Microsoft.UI.Xaml.Controls.NumberBox sender, Microsoft.UI.Xaml.Controls.NumberBoxValueChangedEventArgs args)
    {
        if (_isInitializing) return;
        int? v = double.IsNaN(args.NewValue) ? null : (int?)((int)args.NewValue);
        _vm.SelectedPostEventDelayMs = v;
    }

    private void OnGlobalDelayChanged(Microsoft.UI.Xaml.Controls.NumberBox sender, Microsoft.UI.Xaml.Controls.NumberBoxValueChangedEventArgs args)
    {
        if (_isInitializing) return;
        if (double.IsNaN(args.NewValue)) return;
        _vm.PostEventDelayMs = (int)args.NewValue;
    }

    private void OnSelectionPolicyChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        if (SelectionPolicyCombo.SelectedItem is Microsoft.UI.Xaml.Controls.ComboBoxItem ci && ci.Tag is string tag)
            _vm.SelectionPolicy = tag;
    }

    private void OnNoRepeatChanged(Microsoft.UI.Xaml.Controls.NumberBox sender, Microsoft.UI.Xaml.Controls.NumberBoxValueChangedEventArgs args)
    {
        if (_isInitializing) return;
        if (double.IsNaN(args.NewValue)) return;
        _vm.NoRepeatWindow = (int)args.NewValue;
    }

    private void OnIdleColorChanged(object sender, TextChangedEventArgs e)
    {
        var txt = IdleColorBox.Text;
        if (System.Text.RegularExpressions.Regex.IsMatch(txt, "^#[0-9a-fA-F]{6}$"))
            _vm.IdleColor = txt;
    }

    private void OnColorPickerChanged(Microsoft.UI.Xaml.Controls.ColorPicker sender, Microsoft.UI.Xaml.Controls.ColorChangedEventArgs args)
    {
        var c = args.NewColor;
        string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        IdleColorBox.Text = hex;
    }

    private void OnPickIdleColor(object sender, RoutedEventArgs e)
    {
        IdleColorPicker.Visibility = IdleColorPicker.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnBrowseClicked(object sender, RoutedEventArgs e) { await _vm.BrowseCyclesRootAsync(); CyclesRootTextBox.Text = _vm.CyclesRoot; }

    private async void OnAddSceneClicked(object sender, RoutedEventArgs e)
    {
        await _vm.AddSceneAsync();
        SceneListView.ItemsSource = null;
        SceneListView.ItemsSource = _vm.Scenes;
        if (_vm.SelectedScene != null) SceneListView.SelectedItem = _vm.SelectedScene;
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Saved (debounced 500ms → atomic write + Reload + UpdateConfig)";
    }

#if WINDOWS
    private sealed class WinUIFolderPicker : IFilePicker
    {
        private readonly Func<IntPtr> _hwndProvider;
        private readonly IntPtr _fixed;
        public bool IsZeroHandle => _fixed == IntPtr.Zero && _hwndProvider() == IntPtr.Zero;
        public WinUIFolderPicker(IntPtr hwnd) { _fixed = hwnd; _hwndProvider = () => hwnd; }
        public WinUIFolderPicker(Func<IntPtr> provider) { _fixed = IntPtr.Zero; _hwndProvider = provider; }
        public async Task<string?> PickFolderAsync(string initialPath)
        {
            try
            {
                var picker = new FolderPicker();
                picker.FileTypeFilter.Add("*");
                IntPtr hwnd = _fixed != IntPtr.Zero ? _fixed : _hwndProvider();
                if (hwnd == IntPtr.Zero)
                {
                    try
                    {
                        // Try to get any active window handle via App.Current dispatcher fallback — else zero
                        // Caller should have supplied provider via SettingsWindow constructor
                    }
                    catch { }
                }
                // For WinUI3 FolderPicker, HWND init is REQUIRED else it silently fails (no dialog)
                if (hwnd != IntPtr.Zero)
                {
                    InitializeWithWindow.Initialize(picker, hwnd);
                    Console.WriteLine($"[WinUIFolderPicker] InitializeWithWindow hwnd=0x{hwnd.ToInt64():X}");
                }
                else
                {
                    Console.WriteLine("[WinUIFolderPicker] hwnd zero - picker will silently fail (fix: ensure SettingsWindow activated before Browse)");
                }
                if (!string.IsNullOrEmpty(initialPath) && Directory.Exists(initialPath))
                {
                    try
                    {
                        var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(initialPath));
                        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
                        // Setting SuggestedStartLocation after getting folder is unreliable for FolderPicker, but we try
                    }
                    catch { }
                }
                picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
                var picked = await picker.PickSingleFolderAsync();
                string? result = picked?.Path;
                Console.WriteLine($"[WinUIFolderPicker] picked='{result ?? "null (cancelled)"}'");
                return result;
            }
            catch (Exception ex) { Console.WriteLine($"[WinUIFolderPicker] fail: {ex.Message}"); return null; }
        }
    }
#endif
}
