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

    public SettingsWindow() : this(CreateDefaultViewModel()) { }

    public SettingsWindow(SettingsViewModel vm)
    {
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
        _previewTimer = new DispatcherTimer();
        _previewTimer.Tick += OnPreviewTick;
        this.Activated += OnActivated;
        this.Closed += (_, _) => { try { _previewTimer.Stop(); } catch { } try { _vm.Dispose(); } catch { } Console.WriteLine("[SettingsWindow] Closed"); };
    }

    private static SettingsViewModel CreateDefaultViewModel()
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
        var picker = new WinUIFolderPicker(SyncHandle());
        return new SettingsViewModel(cycleStore, settingsStore, picker, update, debounceMs: 500);
        static IntPtr SyncHandle() => IntPtr.Zero;
    }

    private bool _activated;
    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_activated) return;
        _activated = true;
        await OnLoadedAsync();
    }

    private async Task OnLoadedAsync()
    {
        Console.WriteLine("[SettingsWindow] OnLoadedAsync start");
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
        try
        {
            if (SceneListView?.SelectedItem is SceneListItem item)
            {
                _vm.SelectedScene = item;
                if (FpsBox != null) FpsBox.Value = item.Fps;
                if (HoldLastBox != null) HoldLastBox.Value = item.Config?.HoldLastMs ?? 0;
                if (SceneDelayBox != null) SceneDelayBox.Value = item.Config?.PostEventDelayMs ?? double.NaN;
                if (PreviewSlider != null) { PreviewSlider.Maximum = Math.Max(0, item.Frames.Count - 1); PreviewSlider.Value = _vm.CurrentFrameIndex; }
                UpdatePreviewImage();
                UpdatePreviewInterval();
            }
        }
        catch (Exception ex) { Console.WriteLine($"[SettingsWindow] OnSceneSelectionChanged failed: {ex.Message}"); }
    }

    private void UpdatePreviewImage()
    {
        try
        {
            var path = _vm.CurrentPreviewFramePath;
            if (PreviewImage == null) return;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                PreviewImage.Source = null;
                return;
            }
            try
            {
                var bmp = new BitmapImage(new Uri(path));
                PreviewImage.Source = bmp;
                if (PreviewSlider != null) PreviewSlider.Value = _vm.CurrentFrameIndex;
            }
            catch (Exception ex) { Console.WriteLine($"[SettingsWindow] UpdatePreviewImage failed: {ex.Message}"); }
        }
        catch { }
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

    private void OnLoopToggled(object sender, RoutedEventArgs e) { UpdatePreviewInterval(); }

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
        if (double.IsNaN(args.NewValue)) return;
        int fps = (int)args.NewValue;
        fps = Math.Clamp(fps, 1, 30);
        if (_vm.SelectedScene != null && _vm.SelectedScene.Fps == fps) return;
        _vm.SelectedFps = fps;
        UpdatePreviewInterval();
    }

    private void OnHoldLastChanged(Microsoft.UI.Xaml.Controls.NumberBox sender, Microsoft.UI.Xaml.Controls.NumberBoxValueChangedEventArgs args)
    {
        if (double.IsNaN(args.NewValue)) return;
        _vm.SelectedHoldLastMs = (int)args.NewValue;
    }

    private void OnSceneDelayChanged(Microsoft.UI.Xaml.Controls.NumberBox sender, Microsoft.UI.Xaml.Controls.NumberBoxValueChangedEventArgs args)
    {
        int? v = double.IsNaN(args.NewValue) ? null : (int?)((int)args.NewValue);
        _vm.SelectedPostEventDelayMs = v;
    }

    private void OnGlobalDelayChanged(Microsoft.UI.Xaml.Controls.NumberBox sender, Microsoft.UI.Xaml.Controls.NumberBoxValueChangedEventArgs args)
    {
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
        private readonly IntPtr _hwnd;
        public WinUIFolderPicker(IntPtr hwnd) => _hwnd = hwnd;
        public async Task<string?> PickFolderAsync(string initialPath)
        {
            try
            {
                var picker = new FolderPicker();
                picker.FileTypeFilter.Add("*");
                IntPtr hwnd = _hwnd;
                if (hwnd == IntPtr.Zero)
                {
                    try { hwnd = WindowNative.GetWindowHandle(App.Current); } catch { }
                }
                if (hwnd != IntPtr.Zero)
                    InitializeWithWindow.Initialize(picker, hwnd);
                var folder = await picker.PickSingleFolderAsync();
                return folder?.Path;
            }
            catch { return null; }
        }
    }
#endif
}
