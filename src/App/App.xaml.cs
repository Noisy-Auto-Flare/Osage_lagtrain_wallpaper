using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using OsageLagtrain.App.Cycles;
using OsageLagtrain.App.Desktop;
using OsageLagtrain.App.Shell;
using OsageLagtrain.App.Ui;

namespace OsageLagtrain.App;

public partial class App : Application
{
    private SingleInstanceManager? _singleInstance;
    private ConfigStore? _configStore;
    private DesktopLayerHost? _desktopHost;
    private WindowMonitor.WindowMonitor? _windowMonitor;
    private EnableManager? _enableManager;
    private AutostartManager? _autostart;
    private TrayIcon? _trayLogic;
    private Rendering.WallpaperWindow? _wallpaperWindow;
    private CycleStore? _cycleStore;
    private ISelectionPolicy? _selectionPolicy;
    private Window? _wallpaperHostWindow;
    private Window? _settingsWindow;
    private IntPtr _hwnd = IntPtr.Zero;
    private NativeTray? _nativeTray;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint WM_TRAYICON = 0x8001;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractAssociatedIcon(IntPtr hInst, string lpIconPath, out ushort lpiIcon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _singleInstance = new SingleInstanceManager();
        bool isFirst;
        try { isFirst = _singleInstance.TryAcquire(); }
        catch { isFirst = true; }
        if (!isFirst)
        {
            try { _singleInstance.NotifyFirstInstance(); } catch { }
            Environment.Exit(0);
            return;
        }

        _configStore = new ConfigStore();
        SettingsConfig settings;
        try { settings = _configStore.LoadSettings(); }
        catch { settings = new SettingsConfig(); }

        _desktopHost = new DesktopLayerHost();
        _cycleStore = new CycleStore(settings.CyclesRoot);
        _selectionPolicy = CreateSelectionPolicy(settings);
        _windowMonitor = new WindowMonitor.WindowMonitor(globalPostEventDelayMs: settings.PostEventDelayMs);
        _windowMonitor.WallpaperShouldAdvance += OnWallpaperShouldAdvance;

        _wallpaperHostWindow = new HiddenWallpaperHostWindow();
        _wallpaperHostWindow.Activate();
        _hwnd = WindowNative.GetWindowHandle(_wallpaperHostWindow);

        try
        {
            _desktopHost.Probe();
            _desktopHost.EnsureLayer();
            _desktopHost.Attach(_hwnd);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] DesktopLayerHost attach failed: {ex.Message}");
        }

        try
        {
            _wallpaperWindow = new Rendering.WallpaperWindow(layerHost: _desktopHost, idleColorHex: settings.IdleColor);
            _wallpaperWindow.AttachToDesktop(_hwnd);
            _wallpaperWindow.ShowIdle();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] WallpaperWindow attach failed: {ex.Message}");
        }

        _enableManager = new EnableManager(_desktopHost, _windowMonitor, () => _hwnd);
        _autostart = new AutostartManager();
        _trayLogic = new TrayIcon(_autostart, _enableManager, _singleInstance,
            showSettings: () => ShowSettings(),
            exitAction: () => ExitApp());

        try { _windowMonitor.Start(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[App] WindowMonitor Start failed: {ex.Message}"); }

        try { CreateTrayIcon(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[App] Tray create failed: {ex.Message}"); }

        _wallpaperHostWindow.Closed += (_, _) => Cleanup();

        System.Diagnostics.Debug.WriteLine("[App] OnLaunched complete — tray visible, wallpaper attached, monitor started");
        Console.WriteLine("[App] OnLaunched complete — tray visible, wallpaper attached, monitor started");
    }

    private void CreateTrayIcon()
    {
        if (_trayLogic == null || _hwnd == IntPtr.Zero) return;
        IntPtr hIcon = IntPtr.Zero;
        try
        {
            hIcon = LoadIconW(IntPtr.Zero, (IntPtr)32512);
            if (hIcon == IntPtr.Zero)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                {
                    ushort idx = 0;
                    hIcon = ExtractAssociatedIcon(IntPtr.Zero, exe, out idx);
                }
            }
        }
        catch { }

        var data = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = hIcon,
            szTip = "Osage Lagtrain",
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };
        bool added = false;
        try { added = Shell_NotifyIconW(NIM_ADD, ref data); } catch { }
        if (!added)
        {
            data.uFlags = NIF_ICON | NIF_TIP;
            try { added = Shell_NotifyIconW(NIM_ADD, ref data); } catch { }
        }
        _nativeTray = new NativeTray(_hwnd, data, hIcon);
        System.Diagnostics.Debug.WriteLine($"[App] TrayIcon Show() — Shell_NotifyIcon NIM_ADD={added} hwnd=0x{_hwnd.ToInt64():X}");
        Console.WriteLine($"[App] TrayIcon Show() — Shell_NotifyIcon NIM_ADD={added} hwnd=0x{_hwnd.ToInt64():X}");
        try
        {
            var menu = _trayLogic.BuildMenu();
            foreach (var item in menu)
                Console.WriteLine($"[App] Tray menu: {item.Text} checked={item.isChecked} state=0x{item.checkedState:X}");
        }
        catch { }
    }

    private sealed class NativeTray : IDisposable
    {
        private readonly IntPtr _hwnd;
        private NOTIFYICONDATA _data;
        private readonly IntPtr _hIcon;
        private bool _disposed;
        public NativeTray(IntPtr hwnd, NOTIFYICONDATA data, IntPtr hIcon) { _hwnd = hwnd; _data = data; _hIcon = hIcon; }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { var d = _data; Shell_NotifyIconW(NIM_DELETE, ref d); } catch { }
            try
            {
                var sysIcon = LoadIconW(IntPtr.Zero, (IntPtr)32512);
                if (_hIcon != IntPtr.Zero && _hIcon != sysIcon) DestroyIcon(_hIcon);
            }
            catch { }
        }
    }

    private void ShowSettings()
    {
        var dq = _wallpaperHostWindow?.DispatcherQueue ?? Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (dq == null)
        {
            try { _settingsWindow = new SettingsWindow(); _settingsWindow.Activate(); } catch { }
            return;
        }
        dq.TryEnqueue(() =>
        {
            try
            {
                if (_settingsWindow != null)
                {
                    var hwnd = WindowNative.GetWindowHandle(_settingsWindow);
                    ShowWindow(hwnd, 9);
                    _settingsWindow.Activate();
                    return;
                }
                _settingsWindow = new SettingsWindow();
                _settingsWindow.Closed += (_, _) => _settingsWindow = null;
                _settingsWindow.Activate();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] ShowSettings failed: {ex.Message}");
            }
        });
    }

    private void OnWallpaperShouldAdvance(string monitorId, string exeName)
    {
        try
        {
            if (_cycleStore == null || _configStore == null || _selectionPolicy == null) return;
            var settings = _configStore.LoadSettings();
            var all = _cycleStore.LoadAll();
            if (all.Count == 0) return;

            IReadOnlyList<CycleInfo> eligible;
            if (settings.AppMap != null && !string.IsNullOrEmpty(exeName) && settings.AppMap.TryGetValue(exeName, out var allowedIds))
            {
                var allowedSet = new HashSet<string>(allowedIds, StringComparer.OrdinalIgnoreCase);
                var filtered = all.Where(c => allowedSet.Contains(c.Id)).ToList();
                eligible = filtered.Count > 0 ? filtered : all;
            }
            else
            {
                eligible = all;
            }

            var history = _configStore.LoadHistory();
            var pickedId = _selectionPolicy.Pick(eligible, history, exeName?.ToLowerInvariant(), settings.AppMap);
            if (string.IsNullOrEmpty(pickedId)) return;

            var picked = eligible.FirstOrDefault(c => string.Equals(c.Id, pickedId, StringComparison.Ordinal)) ?? all.FirstOrDefault(c => c.Id == pickedId);
            if (picked == null) return;

            try { _configStore.AppendHistory(picked.Id, settings.NoRepeatWindow); } catch { }
            try { _windowMonitor?.SetPerSceneDelay(picked.Config.PostEventDelayMs); } catch { }

            var dq = _wallpaperHostWindow?.DispatcherQueue ?? Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            if (dq == null) return;
            dq.TryEnqueue(async () =>
            {
                try
                {
                    if (_wallpaperWindow == null || _cycleStore == null) return;
                    var framePaths = _cycleStore.GetFrames(picked.Id);
                    var bytes = new List<byte[]>();
                    foreach (var p in framePaths)
                    {
                        try { bytes.Add(File.ReadAllBytes(p)); } catch { }
                    }
                    if (bytes.Count == 0) return;
                    _wallpaperWindow.SetIdleColor(settings.IdleColor);
                    _wallpaperWindow.Play(picked, bytes);
                    var nextId = _selectionPolicy.Pick(eligible, _configStore.LoadHistory(), exeName?.ToLowerInvariant(), settings.AppMap);
                    if (!string.IsNullOrEmpty(nextId) && nextId != picked.Id)
                    {
                        var next = eligible.FirstOrDefault(c => c.Id == nextId);
                        if (next != null)
                            await _wallpaperWindow.PreloadNextSceneAsync(next);
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[App] OnWallpaperShouldAdvance play failed: {ex.Message}"); }
            });
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[App] OnWallpaperShouldAdvance failed: {ex.Message}"); }
    }

    private static ISelectionPolicy CreateSelectionPolicy(SettingsConfig cfg)
    {
        return cfg.SelectionPolicy switch
        {
            "randomPure" => new RandomPurePolicy(),
            "sequentialByName" => new SequentialByNamePolicy(),
            "sequentialByMtime" => new SequentialByMtimePolicy(),
            _ => new RandomNoRepeatPolicy(cfg.NoRepeatWindow, new Random())
        };
    }

    private void ExitApp()
    {
        try { _windowMonitor?.Dispose(); } catch { }
        try { _desktopHost?.Dispose(); } catch { }
        try { _nativeTray?.Dispose(); } catch { }
        try { _wallpaperWindow?.Dispose(); } catch { }
        try { _singleInstance?.Dispose(); } catch { }
        Environment.Exit(0);
    }

    private void Cleanup()
    {
        try { _nativeTray?.Dispose(); _nativeTray = null; } catch { }
        try { _windowMonitor?.Dispose(); } catch { }
        try { _desktopHost?.Dispose(); } catch { }
        try { _wallpaperWindow?.Dispose(); } catch { }
        try { _singleInstance?.Dispose(); } catch { }
    }
}

public sealed class HiddenWallpaperHostWindow : Window
{
    public HiddenWallpaperHostWindow()
    {
        Title = "Osage Lagtrain Wallpaper";
        Content = new Microsoft.UI.Xaml.Controls.Grid
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xB2, 0xB2, 0xB2))
        };
    }
}

public sealed class MainWindow : Window
{
    public MainWindow()
    {
        Title = "Osage Lagtrain Wallpaper";
        Content = new Microsoft.UI.Xaml.Controls.Grid
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xB2, 0xB2, 0xB2))
        };
    }
}
