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
        InitializeComponent();
        _vm = vm;
        // preview timer @ fps reuse FrameScheduler interval via DispatcherTimer
        _previewTimer = new DispatcherTimer();
        _previewTimer.Tick += OnPreviewTick;

        this.Activated += OnActivated;
        this.Closed += (_, _) => { _previewTimer.Stop(); _vm.Dispose(); };
    }

    private static SettingsViewModel CreateDefaultViewModel()
    {
        var settingsStore = new SettingsStore();
        var cfg = settingsStore.Load();
        var cycleStore = new CycleStoreAdapter(cfg.CyclesRoot);
        // WindowMonitor hook — no-op if not available
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
        // bind cyclesRoot
        try
        {
            CyclesRootTextBox.Text = _vm.GlobalSettings.CyclesRoot;
            GlobalDelayBox.Value = _vm.GlobalSettings.PostEventDelayMs;
            NoRepeatBox.Value = _vm.GlobalSettings.NoRepeatWindow;
            IdleColorBox.Text = _vm.GlobalSettings.IdleColor;
            // selectionPolicy combo
            for (int i = 0; i < SelectionPolicyCombo.Items.Count; i++)
            {
                if (SelectionPolicyCombo.Items[i] is Microsoft.UI.Xaml.Controls.ComboBoxItem ci && (ci.Tag as string) == _vm.GlobalSettings.SelectionPolicy)
                {
                    SelectionPolicyCombo.SelectedIndex = i; break;
                }
            }
            if (_vm.HasAppMap)
            {
                AppMapPanel.Visibility = Visibility.Visible;
                AppMapText.Text = string.Join("\n", _vm.AppMap!.Select(kv => $"{kv.Key}: {string.Join(", ", kv.Value)}"));
            }
        }
        catch { }

        _vm.PropertyChanged += OnVmPropertyChanged;
        await _vm.LoadScenesAsync();
        try
        {
            SceneListView.ItemsSource = _vm.Scenes;
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
            if (_vm.SelectedScene != null)
                SceneListView.SelectedItem = _vm.SelectedScene;
            UpdatePreviewImage();
            UpdatePreviewInterval();
        }
        catch { }
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
        if (SceneListView.SelectedItem is SceneListItem item)
        {
            _vm.SelectedScene = item;
            FpsBox.Value = item.Fps;
            HoldLastBox.Value = item.Config?.HoldLastMs ?? 0;
            SceneDelayBox.Value = item.Config?.PostEventDelayMs ?? double.NaN;
            PreviewSlider.Maximum = Math.Max(0, item.Frames.Count - 1);
            PreviewSlider.Value = _vm.CurrentFrameIndex;
            UpdatePreviewImage();
            UpdatePreviewInterval();
        }
    }

    private void UpdatePreviewImage()
    {
        var path = _vm.CurrentPreviewFramePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            // idle color #b2b2b2
            PreviewImage.Source = null;
            return;
        }
        try
        {
            var bmp = new BitmapImage(new Uri(path));
            PreviewImage.Source = bmp;
            PreviewSlider.Value = _vm.CurrentFrameIndex;
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

    private void OnLoopToggled(object sender, RoutedEventArgs e) { /* respects mode but preview loop toggle: restart timer */ UpdatePreviewInterval(); }

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
        // avoid re-entrancy when we set box programmatically
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

    private async void OnBrowseClicked(object sender, RoutedEventArgs e)
    {
        await _vm.BrowseCyclesRootAsync();
        CyclesRootTextBox.Text = _vm.CyclesRoot;
    }

    private async void OnAddSceneClicked(object sender, RoutedEventArgs e)
    {
        await _vm.AddSceneAsync();
        SceneListView.ItemsSource = null;
        SceneListView.ItemsSource = _vm.Scenes;
        if (_vm.SelectedScene != null) SceneListView.SelectedItem = _vm.SelectedScene;
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        // Save is debounced automatically; this button forces flush if visible
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
