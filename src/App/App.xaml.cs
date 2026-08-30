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
    private const uint WM_COMMAND = 0x0111;
    private const uint WM_NULL = 0x0000;
    private const int GWLP_WNDPROC = -4;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_RBUTTONDBLCLK = 0x0206;
    private const uint MF_STRING = 0x00000000;
    private const uint MF_CHECKED = 0x00000008;
    private const uint TPM_LEFTALIGN = 0x0000;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;
    private const int ID_TRAY_SHOW = 1001;
    private const int ID_TRAY_ENABLE = 1002;
    private const int ID_TRAY_AUTOSTART = 1003;
    private const int ID_TRAY_EXIT = 1004;

    private IntPtr _oldWndProc = IntPtr.Zero;
    private WndProcDelegate? _wndProcDelegate;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLongW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLongW(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallWindowProcW(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DONOTROUND = 1;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_BOTTOM = new(1);

    private static void TryDisableRoundedCorners(IntPtr hwnd)
    {
        try { int pref = DWMWCP_DONOTROUND; DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int)); } catch { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

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

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
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
        EnsureWindowBorderless(_hwnd);
        HideHostWindowImmediate(_hwnd);

        // Show idle #b2b2b2 immediately on UI thread — do not wait for desktop probe
        try
        {
            _wallpaperWindow = new Rendering.WallpaperWindow(layerHost: _desktopHost, idleColorHex: settings.IdleColor);
            try
            {
                if (_wallpaperHostWindow is HiddenWallpaperHostWindow hw)
                    _wallpaperWindow.BindHostImage(hw.WallpaperImage);
            }
            catch { }
            _wallpaperWindow.ShowIdle();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] WallpaperWindow creation failed: {ex.Message}");
        }

        // Create tray and monitor early so they appear before long EnsureLayer
        _enableManager = new EnableManager(_desktopHost, _windowMonitor, () => _hwnd);
        _autostart = new AutostartManager();
        _trayLogic = new TrayIcon(_autostart, _enableManager, _singleInstance,
            showSettings: () => ShowSettings(),
            exitAction: () => ExitApp());

        try { _windowMonitor.Start(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[App] WindowMonitor Start failed: {ex.Message}"); }

        try { CreateTrayIcon(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[App] Tray create failed: {ex.Message}"); }
        try { InstallTrayWndProc(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[App] WndProc hook failed: {ex.Message}"); }

        _wallpaperHostWindow.Closed += (_, _) => Cleanup();

        System.Diagnostics.Debug.WriteLine("[App] OnLaunched initial UI ready — tray visible, idle #b2b2b2 shown, desktop attach pending (async)");
        Console.WriteLine("[App] OnLaunched initial UI ready — tray visible, idle #b2b2b2 shown, desktop attach pending (async)");

        // Offload Probe/EnsureLayer/Attach to background — never block dispatcher (20x300ms = 6s)
        var hwndCopy = _hwnd;
        var desktopHostCopy = _desktopHost;
        var wallpaperWindowCopy = _wallpaperWindow;
        var dispatcher = _wallpaperHostWindow.DispatcherQueue;
        _ = Task.Run(async () =>
        {
            try
            {
                desktopHostCopy.Probe();
                bool layerOk = await desktopHostCopy.EnsureLayerAsync().ConfigureAwait(false);
                if (!layerOk)
                    System.Diagnostics.Debug.WriteLine("[App] EnsureLayerAsync exhausted 20 retries (6s) — layer pending, healing will retry");

                // Marshal Attach + WallpaperWindow attach back to UI thread (DComp requires UI thread)
                if (dispatcher != null)
                {
                    dispatcher.TryEnqueue(() =>
                    {
                        bool attached = false;
                        try
                        {
                            attached = desktopHostCopy.Attach(hwndCopy);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[App] background Attach failed: {ex.Message}");
                        }
                        if (!attached)
                        {
                            // Fallback: Attach failed (WorkerW not found on classic / missing SHELLDLL_DefView on raised) — do NOT hide forever.
                            // Show fullscreen borderless window as fallback so idleColor #b2b2b2 and wallpaper frames still appear.
                            // Keep TOOLWINDOW + not in switchers so it doesn't pollute Alt+Tab/taskbar.
                            try { EnsureWindowBorderless(hwndCopy); } catch { }
                            try { EnsureWallpaperBehindDesktop(hwndCopy); } catch { }
                            try { ShowWindow(hwndCopy, 8); } catch { } // SW_SHOWNA fallback visible borderless
                            System.Diagnostics.Debug.WriteLine($"[App] Attach returned false - fallback SW_SHOWNA shown hwnd=0x{hwndCopy.ToInt64():X} (WorkerW/DefView not found, fallback keeps wallpaper visible)");
                            Console.WriteLine($"[App] Attach returned false - fallback SW_SHOWNA shown hwnd=0x{hwndCopy.ToInt64():X}");
                        }
                        else
                        {
                            // Success: ensure wallpaper is visible behind icons but still hidden from taskbar/Alt+Tab
                            try { EnsureWallpaperBehindDesktop(hwndCopy); } catch { }
                        }
                        try
                        {
                            wallpaperWindowCopy?.AttachToDesktop(hwndCopy);
                            wallpaperWindowCopy?.ShowIdle();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[App] background WallpaperWindow AttachToDesktop failed: {ex.Message}");
                        }
                        if (attached)
                        {
                            try { EnsureWallpaperBehindDesktop(hwndCopy); } catch { }
                        }
                    });
                }
                else
                {
                    bool attached = false;
                    try { attached = desktopHostCopy.Attach(hwndCopy); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[App] background Attach (no dispatcher) failed: {ex.Message}"); }
                    if (!attached)
                    {
                        try { EnsureWallpaperBehindDesktop(hwndCopy); } catch { }
                        try { ShowWindow(hwndCopy, 8); } catch { }
                    }
                    else
                    {
                        try { EnsureWallpaperBehindDesktop(hwndCopy); } catch { }
                    }
                    try { wallpaperWindowCopy?.AttachToDesktop(hwndCopy); wallpaperWindowCopy?.ShowIdle(); } catch { }
                    if (attached)
                    {
                        try { EnsureWallpaperBehindDesktop(hwndCopy); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] background desktop setup failed: {ex.Message}");
            }
        });

        System.Diagnostics.Debug.WriteLine("[App] OnLaunched complete — tray visible, wallpaper pending attach, monitor started");
        Console.WriteLine("[App] OnLaunched complete — tray visible, wallpaper pending attach, monitor started");
        await Task.CompletedTask;
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

    private void InstallTrayWndProc()
    {
        if (_hwnd == IntPtr.Zero) return;
        if (_oldWndProc != IntPtr.Zero) return;
        _wndProcDelegate = WndProc;
        IntPtr newWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
        _oldWndProc = GetWindowLongPtrW(_hwnd, GWLP_WNDPROC);
        IntPtr prev = SetWindowLongPtrW(_hwnd, GWLP_WNDPROC, newWndProc);
        if (prev != IntPtr.Zero) _oldWndProc = prev;
        System.Diagnostics.Debug.WriteLine($"[App] WndProc hook installed hwnd=0x{_hwnd.ToInt64():X} old=0x{_oldWndProc.ToInt64():X}");
        Console.WriteLine($"[App] WndProc hook installed hwnd=0x{_hwnd.ToInt64():X}");
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            // SingleInstance registered message first
            if (_trayLogic != null && _trayLogic.HandleWindowMessage(msg, wParam, lParam))
                return IntPtr.Zero;
            if (_singleInstance != null && msg == _singleInstance.ShowSettingsMessageId && msg != 0)
            {
                try { ShowSettings(); } catch { }
                return IntPtr.Zero;
            }

            if (msg == WM_TRAYICON)
            {
                uint trayId = (uint)wParam.ToInt64();
                int mouseMsg = lParam.ToInt32();
                // also handle low-word extraction for 64-bit
                if (trayId == 1)
                {
                    if (mouseMsg == WM_RBUTTONUP)
                    {
                        ShowTrayMenu();
                        return IntPtr.Zero;
                    }
                    if (mouseMsg == WM_LBUTTONUP)
                    {
                        ShowTrayMenu();
                        return IntPtr.Zero;
                    }
                    if (mouseMsg == WM_LBUTTONDBLCLK)
                    {
                        try { ShowSettings(); } catch { }
                        return IntPtr.Zero;
                    }
                    if (mouseMsg == WM_RBUTTONDBLCLK)
                    {
                        ShowTrayMenu();
                        return IntPtr.Zero;
                    }
                }
            }

            if (msg == WM_COMMAND)
            {
                int id = wParam.ToInt32() & 0xFFFF;
                if (id >= ID_TRAY_SHOW && id <= ID_TRAY_EXIT)
                {
                    HandleTrayMenuCommand(id);
                    return IntPtr.Zero;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] WndProc error msg=0x{msg:X} {ex.Message}");
        }

        if (_oldWndProc != IntPtr.Zero)
        {
            try { return CallWindowProcW(_oldWndProc, hWnd, msg, wParam, lParam); } catch { }
        }
        return IntPtr.Zero;
    }

    private void ShowTrayMenu()
    {
        if (_trayLogic == null || _hwnd == IntPtr.Zero) return;
        POINT pt;
        try { GetCursorPos(out pt); } catch { pt = new POINT { X = 0, Y = 0 }; }
        IntPtr hMenu = IntPtr.Zero;
        try
        {
            hMenu = CreatePopupMenu();
            if (hMenu == IntPtr.Zero) return;
            var menu = _trayLogic.BuildMenu();
            // menu order: Show Settings, Enable, Autostart, Exit
            bool enableChecked = _trayLogic.IsEnableChecked;
            bool autostartChecked = _trayLogic.IsAutostartChecked;
            AppendMenuW(hMenu, MF_STRING, (IntPtr)ID_TRAY_SHOW, "Show Settings");
            AppendMenuW(hMenu, enableChecked ? (MF_STRING | MF_CHECKED) : MF_STRING, (IntPtr)ID_TRAY_ENABLE, "Enable");
            AppendMenuW(hMenu, autostartChecked ? (MF_STRING | MF_CHECKED) : MF_STRING, (IntPtr)ID_TRAY_AUTOSTART, "Autostart");
            AppendMenuW(hMenu, MF_STRING, (IntPtr)ID_TRAY_EXIT, "Exit");
            try { SetForegroundWindow(_hwnd); } catch { }
            int cmd = TrackPopupMenu(hMenu, TPM_LEFTALIGN | TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
            // Required to clear stuck menu
            try { PostMessageW(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero); } catch { }
            if (cmd != 0)
                HandleTrayMenuCommand(cmd);
            else
                System.Diagnostics.Debug.WriteLine($"[App] ShowTrayMenu dismissed pt={pt.X},{pt.Y}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] ShowTrayMenu failed: {ex.Message}");
        }
        finally
        {
            if (hMenu != IntPtr.Zero)
                try { DestroyMenu(hMenu); } catch { }
        }
    }

    private void HandleTrayMenuCommand(int id)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[App] Tray WM_COMMAND id={id}");
            Console.WriteLine($"[App] Tray WM_COMMAND id={id}");
            switch (id)
            {
                case ID_TRAY_SHOW:
                    ShowSettings();
                    break;
                case ID_TRAY_ENABLE:
                    // Fire-and-forget async — never block Dispatcher (was 20×300ms=6s hang)
                    if (_enableManager != null)
                    {
                        _ = _enableManager.ToggleAsync();
                    }
                    else
                    {
                        _trayLogic?.ToggleEnable();
                    }
                    System.Diagnostics.Debug.WriteLine($"[App] ToggleEnable dispatched async IsEnabled={_enableManager?.IsEnabled}");
                    Console.WriteLine($"[App] ToggleEnable dispatched async IsEnabled={_enableManager?.IsEnabled}");
                    break;
                case ID_TRAY_AUTOSTART:
                    _trayLogic?.ToggleAutostart();
                    break;
                case ID_TRAY_EXIT:
                    ExitApp();
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] HandleTrayMenuCommand failed id={id} {ex.Message}");
        }
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
        Console.WriteLine("[App] ShowSettings opening");
        System.Diagnostics.Debug.WriteLine("[App] ShowSettings opening");
        try
        {
            var dq = _wallpaperHostWindow?.DispatcherQueue;
            if (dq == null)
            {
                try { dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread(); } catch { dq = null; }
            }
            // If we have a dispatcher and we're not on its thread, enqueue; otherwise execute directly.
            bool hasAccess = false;
            try { hasAccess = dq?.HasThreadAccess ?? false; } catch { hasAccess = false; }
            if (dq != null && !hasAccess)
            {
                bool enqueued = false;
                try { enqueued = dq.TryEnqueue(() => ShowSettingsCore()); } catch (Exception ex) { Console.WriteLine($"[App] ShowSettings TryEnqueue threw: {ex}"); enqueued = false; }
                Console.WriteLine($"[App] ShowSettings TryEnqueue={enqueued} hasAccess={hasAccess}");
                System.Diagnostics.Debug.WriteLine($"[App] ShowSettings TryEnqueue={enqueued}");
                if (enqueued) return;
                Console.WriteLine("[App] ShowSettings TryEnqueue failed — falling back to direct core");
            }
            ShowSettingsCore();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[App] ShowSettings failed: {ex}");
            System.Diagnostics.Debug.WriteLine($"[App] ShowSettings failed: {ex}");
            try { MessageBoxW(IntPtr.Zero, $"Show Settings failed:\n{ex.Message}\n{ex}", "Osage Lagtrain", 0x10); } catch { }
        }
    }

    private void ShowSettingsCore()
    {
        Console.WriteLine("[App] ShowSettingsCore start");
        try
        {
            if (_settingsWindow != null)
            {
                Console.WriteLine("[App] ShowSettingsCore reactivating existing window");
                try
                {
                    var hwnd = WindowNative.GetWindowHandle(_settingsWindow);
                    Console.WriteLine($"[App] ShowSettingsCore existing hwnd=0x{hwnd.ToInt64():X} attempting ShowWindow/Activate");
                    try { ShowWindow(hwnd, 9); } catch (Exception ex) { Console.WriteLine($"[App] ShowWindow restore failed: {ex.Message}"); }
                    try
                    {
                        var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                        var aw = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id);
                        if (aw != null) { aw.IsShownInSwitchers = true; Console.WriteLine("[App] ShowSettingsCore set IsShownInSwitchers=true"); }
                    }
                    catch (Exception ex) { Console.WriteLine($"[App] AppWindow ensure visible failed: {ex.Message}"); }
                    _settingsWindow.Activate();
                    try { ShowWindow(hwnd, 9); } catch { }
                    // Bring to foreground
                    try { SetForegroundWindow(hwnd); } catch { }
                    Console.WriteLine("[App] ShowSettingsCore re-activated existing window ok");
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[App] ShowSettingsCore reactivate failed: {ex} — recreating");
                    try { _settingsWindow = null; } catch { }
                }
            }
            Console.WriteLine("[App] ShowSettingsCore creating new SettingsWindow");
            SettingsWindow win;
            try
            {
                win = new SettingsWindow();
                Console.WriteLine("[App] ShowSettingsCore SettingsWindow constructed ok");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[App] ShowSettingsCore SettingsWindow ctor failed: {ex}");
                System.Diagnostics.Debug.WriteLine($"[App] SettingsWindow ctor failed: {ex}");
                // Fallback simple window so user still gets visible feedback
                try
                {
                    var fallback = new Window();
                    fallback.Title = "Osage Lagtrain — Settings (fallback)";
                    fallback.Content = new Microsoft.UI.Xaml.Controls.TextBlock { Text = $"Settings failed to load:\n{ex.Message}\n\n{ex}", TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap, Margin = new Microsoft.UI.Xaml.Thickness(16) };
                    fallback.Activate();
                    try
                    {
                        var fhwnd = WindowNative.GetWindowHandle(fallback);
                        ShowWindow(fhwnd, 9);
                        var fid = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(fhwnd);
                        var faw = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(fid);
                        if (faw != null) faw.IsShownInSwitchers = true;
                    }
                    catch { }
                    Console.WriteLine("[App] ShowSettingsCore fallback window shown");
                }
                catch (Exception ex2) { Console.WriteLine($"[App] fallback window failed: {ex2}"); }
                try { MessageBoxW(IntPtr.Zero, $"Show Settings failed:\n{ex.Message}\n\n{ex}", "Osage Lagtrain", 0x10); } catch { }
                return;
            }
            _settingsWindow = win;
            _settingsWindow.Closed += (_, _) => { Console.WriteLine("[App] SettingsWindow closed"); _settingsWindow = null; };
            try
            {
                _settingsWindow.Activate();
                Console.WriteLine("[App] ShowSettingsCore Activate called");
            }
            catch (Exception ex) { Console.WriteLine($"[App] Activate threw: {ex}"); throw; }
            try
            {
                var hwnd2 = WindowNative.GetWindowHandle(_settingsWindow);
                Console.WriteLine($"[App] ShowSettingsCore new hwnd=0x{hwnd2.ToInt64():X} ensuring visible");
                try { ShowWindow(hwnd2, 9); } catch (Exception ex) { Console.WriteLine($"[App] ShowWindow new failed: {ex.Message}"); }
                try
                {
                    var id2 = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd2);
                    var aw2 = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id2);
                    if (aw2 != null) { aw2.IsShownInSwitchers = true; Console.WriteLine("[App] new window IsShownInSwitchers=true"); }
                }
                catch (Exception ex) { Console.WriteLine($"[App] AppWindow visible new failed: {ex.Message}"); }
                try { SetForegroundWindow(hwnd2); } catch { }
                // Second activate to ensure foreground
                try { _settingsWindow.Activate(); } catch { }
            }
            catch (Exception ex) { Console.WriteLine($"[App] post-Activate visibility failed: {ex.Message}"); }
            Console.WriteLine("[App] ShowSettings opened ok");
            System.Diagnostics.Debug.WriteLine("[App] ShowSettings opened ok");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[App] ShowSettingsCore failed: {ex}");
            System.Diagnostics.Debug.WriteLine($"[App] ShowSettingsCore failed: {ex}");
            try { MessageBoxW(IntPtr.Zero, $"Show Settings failed:\n{ex.Message}\n{ex}", "Osage Lagtrain", 0x10); } catch { }
        }
    }

    private void OnWallpaperShouldAdvance(string monitorId, string exeName)
    {
        try
        {
            string logPrefix = $"[App] WallpaperShouldAdvance monitor={monitorId} exe={exeName}";
            System.Diagnostics.Debug.WriteLine(logPrefix);
            Console.WriteLine(logPrefix);
            Console.WriteLine($"[App] WallpaperShouldAdvance — _cycleStore={_cycleStore != null} _configStore={_configStore != null} _selectionPolicy={_selectionPolicy != null} _wallpaperWindow={_wallpaperWindow != null} _hwnd=0x{_hwnd.ToInt64():X}");
            if (_cycleStore == null || _configStore == null || _selectionPolicy == null)
            {
                Console.WriteLine("[App] WallpaperShouldAdvance aborted: store/policy null");
                return;
            }
            SettingsConfig settings;
            try { settings = _configStore.LoadSettings(); Console.WriteLine($"[App] WallpaperShouldAdvance settings cyclesRoot={settings.CyclesRoot} idleColor={settings.IdleColor} policy={settings.SelectionPolicy} noRepeat={settings.NoRepeatWindow}"); } catch (Exception ex) { Console.WriteLine($"[App] LoadSettings failed: {ex}"); return; }
            IReadOnlyList<CycleInfo> all;
            try { all = _cycleStore.LoadAll(); Console.WriteLine($"[App] WallpaperShouldAdvance LoadAll found {all.Count} scenes: {string.Join(",", all.Select(c => c.Id))} (root={_cycleStore.CyclesRoot})"); } catch (Exception ex) { Console.WriteLine($"[App] CycleStore LoadAll threw: {ex}"); System.Diagnostics.Debug.WriteLine($"[App] LoadAll threw: {ex}"); return; }
            if (all.Count == 0)
            {
                Console.WriteLine($"[App] WallpaperShouldAdvance no scenes found in {_cycleStore.CyclesRoot} — check cycles\\1\\scene.json exists and valid; dirs={string.Join(",", Directory.Exists(_cycleStore.CyclesRoot) ? Directory.GetDirectories(_cycleStore.CyclesRoot).Select(Path.GetFileName) : new[]{ "(root missing)" })}");
                System.Diagnostics.Debug.WriteLine("[App] WallpaperShouldAdvance no scenes");
                return;
            }

            IReadOnlyList<CycleInfo> eligible;
            if (settings.AppMap != null && !string.IsNullOrEmpty(exeName) && settings.AppMap.TryGetValue(exeName, out var allowedIds))
            {
                var allowedSet = new HashSet<string>(allowedIds, StringComparer.OrdinalIgnoreCase);
                var filtered = all.Where(c => allowedSet.Contains(c.Id)).ToList();
                Console.WriteLine($"[App] WallpaperShouldAdvance appMap filter exe={exeName} allowed={string.Join(",", allowedIds)} filtered={filtered.Count}");
                eligible = filtered.Count > 0 ? filtered : all;
                if (filtered.Count == 0) Console.WriteLine("[App] WallpaperShouldAdvance appMap filtered 0 — fallback to all");
            }
            else
            {
                Console.WriteLine($"[App] WallpaperShouldAdvance no appMap filter — eligible=all ({all.Count}) exe={exeName} appMap={(settings.AppMap==null ? "null" : settings.AppMap.Count.ToString())}");
                eligible = all;
            }

            var history = _configStore.LoadHistory();
            Console.WriteLine($"[App] WallpaperShouldAdvance history recent=[{string.Join(",", history.Recent)}] cursor={history.MtimeCursor}");
            var pickedId = _selectionPolicy.Pick(eligible, history, exeName?.ToLowerInvariant(), settings.AppMap);
            Console.WriteLine($"[App] WallpaperShouldAdvance pickedId={pickedId ?? "(null)"} from eligible=[{string.Join(",", eligible.Select(c=>c.Id))}]");
            if (string.IsNullOrEmpty(pickedId))
            {
                Console.WriteLine("[App] WallpaperShouldAdvance pickedId empty — abort");
                return;
            }

            var picked = eligible.FirstOrDefault(c => string.Equals(c.Id, pickedId, StringComparison.Ordinal)) ?? all.FirstOrDefault(c => c.Id == pickedId);
            if (picked == null)
            {
                Console.WriteLine($"[App] WallpaperShouldAdvance picked id={pickedId} not found in eligible/all — abort");
                return;
            }
            Console.WriteLine($"[App] WallpaperShouldAdvance picked scene id={picked.Id} title={picked.Title} fps={picked.Config.Fps} frames={picked.Frames.Count} dir={picked.DirPath}");

            try { _configStore.AppendHistory(picked.Id, settings.NoRepeatWindow); Console.WriteLine($"[App] AppendHistory {picked.Id} ok"); } catch (Exception ex) { Console.WriteLine($"[App] AppendHistory failed: {ex.Message}"); }
            try { _windowMonitor?.SetPerSceneDelay(picked.Config.PostEventDelayMs); Console.WriteLine($"[App] SetPerSceneDelay {(picked.Config.PostEventDelayMs?.ToString() ?? "null")}"); } catch (Exception ex) { Console.WriteLine($"[App] SetPerSceneDelay failed: {ex.Message}"); }

            var dq = _wallpaperHostWindow?.DispatcherQueue;
            if (dq == null) { try { dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread(); } catch { dq = null; } }
            Console.WriteLine($"[App] WallpaperShouldAdvance dispatching Play to UI thread dq={(dq==null?"null":"ok")} hasAccess={(dq?.HasThreadAccess.ToString() ?? "n/a")}");
            if (dq == null)
            {
                Console.WriteLine("[App] WallpaperShouldAdvance no dispatcher — trying direct Play on calling thread");
                try
                {
                    if (_wallpaperWindow != null)
                    {
                        var fps = _cycleStore.GetFrames(picked.Id);
                        var b = new List<byte[]>();
                        foreach (var p in fps) { try { b.Add(File.ReadAllBytes(p)); Console.WriteLine($"[App] Read frame {p} {b.Last().Length} bytes"); } catch (Exception ex) { Console.WriteLine($"[App] Read frame failed {p}: {ex.Message}"); } }
                        Console.WriteLine($"[App] Direct Play bytes={b.Count} before Play");
                        _wallpaperWindow.SetIdleColor(settings.IdleColor);
                        _wallpaperWindow.Play(picked, b);
                        Console.WriteLine($"[App] Direct Play ok scene={picked.Id} frames={b.Count}");
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[App] Direct Play failed: {ex}"); System.Diagnostics.Debug.WriteLine($"[App] Direct Play failed: {ex}"); }
                return;
            }
            bool enq = false;
            try { enq = dq.TryEnqueue(async () =>
            {
                try
                {
                    Console.WriteLine($"[App] OnWallpaperShouldAdvance dispatcher Play start scene={picked.Id}");
                    if (_wallpaperWindow == null || _cycleStore == null) { Console.WriteLine("[App] Play aborted: wallpaperWindow/cycleStore null inside dispatch"); return; }
                    var framePaths = _cycleStore.GetFrames(picked.Id);
                    Console.WriteLine($"[App] GetFrames {picked.Id} returned {framePaths.Count}: {string.Join(",", framePaths.Select(Path.GetFileName))}");
                    var bytes = new List<byte[]>();
                    foreach (var p in framePaths)
                    {
                        try { var bb = File.ReadAllBytes(p); bytes.Add(bb); Console.WriteLine($"[App] Read frame ok {Path.GetFileName(p)} {bb.Length} bytes"); } catch (Exception ex) { Console.WriteLine($"[App] Read frame failed {p}: {ex.Message}"); }
                    }
                    if (bytes.Count == 0) { Console.WriteLine("[App] No bytes to play — abort"); return; }
                    Console.WriteLine($"[App] Calling SetIdleColor {settings.IdleColor} and Play scene={picked.Id} bytes={bytes.Count}");
                    _wallpaperWindow.SetIdleColor(settings.IdleColor);
                    _wallpaperWindow.Play(picked, bytes);
                    Console.WriteLine($"[App] Play dispatched ok scene={picked.Id} framesRendered={_wallpaperWindow.FramesRendered} isPlaying={_wallpaperWindow.IsPlaying} isIdle={_wallpaperWindow.IsIdle}");
                    System.Diagnostics.Debug.WriteLine($"[App] Play dispatched ok scene={picked.Id}");
                    try
                    {
                        var nextId = _selectionPolicy.Pick(eligible, _configStore.LoadHistory(), exeName?.ToLowerInvariant(), settings.AppMap);
                        Console.WriteLine($"[App] Preload nextId={nextId ?? "(null)"}");
                        if (!string.IsNullOrEmpty(nextId) && nextId != picked.Id)
                        {
                            var next = eligible.FirstOrDefault(c => c.Id == nextId);
                            if (next != null) { Console.WriteLine($"[App] Preloading next scene {next.Id}"); await _wallpaperWindow.PreloadNextSceneAsync(next); Console.WriteLine("[App] Preload done"); }
                        }
                    }
                    catch (Exception ex) { Console.WriteLine($"[App] Preload failed: {ex.Message}"); }
                }
                catch (Exception ex) { Console.WriteLine($"[App] OnWallpaperShouldAdvance play failed: {ex}"); System.Diagnostics.Debug.WriteLine($"[App] OnWallpaperShouldAdvance play failed: {ex}"); }
            }); } catch (Exception ex) { Console.WriteLine($"[App] TryEnqueue threw: {ex}"); }
            Console.WriteLine($"[App] WallpaperShouldAdvance TryEnqueue={enq}");
            if (!enq)
            {
                Console.WriteLine("[App] TryEnqueue false — attempting direct Play fallback");
                try
                {
                    if (_wallpaperWindow != null)
                    {
                        var fps2 = _cycleStore.GetFrames(picked.Id);
                        var b2 = new List<byte[]>();
                        foreach (var p in fps2) { try { b2.Add(File.ReadAllBytes(p)); } catch { } }
                        if (b2.Count > 0) { _wallpaperWindow.SetIdleColor(settings.IdleColor); _wallpaperWindow.Play(picked, b2); Console.WriteLine($"[App] Fallback Play ok scene={picked.Id}"); }
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[App] Fallback Play failed: {ex}"); }
            }
        }
        catch (Exception ex) { Console.WriteLine($"[App] OnWallpaperShouldAdvance failed: {ex}"); System.Diagnostics.Debug.WriteLine($"[App] OnWallpaperShouldAdvance failed: {ex}"); }
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
        try
        {
            if (_hwnd != IntPtr.Zero && _oldWndProc != IntPtr.Zero)
                try { SetWindowLongPtrW(_hwnd, GWLP_WNDPROC, _oldWndProc); } catch { }
        }
        catch { }
        try { _windowMonitor?.Dispose(); } catch { }
        try { _desktopHost?.Dispose(); } catch { }
        try { _nativeTray?.Dispose(); } catch { }
        try { _wallpaperWindow?.Dispose(); } catch { }
        try { _singleInstance?.Dispose(); } catch { }
        Environment.Exit(0);
    }

    private void Cleanup()
    {
        try
        {
            if (_hwnd != IntPtr.Zero && _oldWndProc != IntPtr.Zero)
                try { SetWindowLongPtrW(_hwnd, GWLP_WNDPROC, _oldWndProc); _oldWndProc = IntPtr.Zero; } catch { }
        }
        catch { }
        try { _nativeTray?.Dispose(); _nativeTray = null; } catch { }
        try { _windowMonitor?.Dispose(); } catch { }
        try { _desktopHost?.Dispose(); } catch { }
        try { _wallpaperWindow?.Dispose(); } catch { }
        try { _singleInstance?.Dispose(); } catch { }
    }

    internal static void EnsureWindowBorderless(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        // AppWindow: extend content into title bar, no border/caption, not in switchers
        try
        {
            var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id);
            if (appWindow != null)
            {
                appWindow.IsShownInSwitchers = false;
                try { appWindow.TitleBar.ExtendsContentIntoTitleBar = true; } catch { }
                try
                {
                    if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter op)
                    {
                        op.IsMaximizable = false;
                        op.IsMinimizable = false;
                        op.IsResizable = false;
                        op.IsAlwaysOnTop = false;
                        try { op.SetBorderAndTitleBar(false, false); } catch { }
                    }
                    else
                    {
                        try
                        {
                            var presenter = Microsoft.UI.Windowing.OverlappedPresenter.Create();
                            presenter.IsMaximizable = false;
                            presenter.IsMinimizable = false;
                            presenter.IsResizable = false;
                            presenter.IsAlwaysOnTop = false;
                            presenter.SetBorderAndTitleBar(false, false);
                            appWindow.SetPresenter(presenter);
                        }
                        catch { }
                    }
                }
                catch { }
                try
                {
                    var tb = appWindow.TitleBar;
                    tb.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                    tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
                    tb.ButtonForegroundColor = Microsoft.UI.Colors.Transparent;
                }
                catch { }
            }
        }
        catch { }

        // Win32: remove WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_BORDER | WS_DLGFRAME | WS_CHILD, add WS_POPUP
        try
        {
            const int GWL_STYLE = -16;
            const int GWL_EXSTYLE = -20;
            const long WS_CAPTION = 0x00C00000L;
            const long WS_THICKFRAME = 0x00040000L;
            const long WS_SYSMENU = 0x00080000L;
            const long WS_MINIMIZEBOX = 0x00020000L;
            const long WS_MAXIMIZEBOX = 0x00010000L;
            const long WS_BORDER = 0x00800000L;
            const long WS_DLGFRAME = 0x00400000L;
            const long WS_CHILD = 0x40000000L;
            const long WS_POPUP = unchecked((long)0x80000000);
            var style = GetWindowLongPtrW(hwnd, GWL_STYLE);
            long s = style.ToInt64();
            s &= ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_BORDER | WS_DLGFRAME | WS_CHILD);
            s |= WS_POPUP;
            SetWindowLongPtrW(hwnd, GWL_STYLE, new IntPtr(s));

            var ex = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
            long exVal = ex.ToInt64();
            exVal |= 0x00000080L; // WS_EX_TOOLWINDOW
            exVal &= ~0x00040000L; // WS_EX_APPWINDOW
            SetWindowLongPtrW(hwnd, GWL_EXSTYLE, new IntPtr(exVal));
        }
        catch { }
        // Remove rounded corners (Win11 DWM) — must be after style changes
        try { TryDisableRoundedCorners(hwnd); } catch { }
    }

    private static void HideHostWindowImmediate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            EnsureWindowBorderless(hwnd);
            ShowWindow(hwnd, 0); // SW_HIDE
            System.Diagnostics.Debug.WriteLine($"[App] HideHostWindowImmediate SW_HIDE + TOOLWINDOW borderless hwnd=0x{hwnd.ToInt64():X}");
            Console.WriteLine($"[App] HideHostWindowImmediate hidden borderless hwnd=0x{hwnd.ToInt64():X}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] HideHostWindowImmediate failed: {ex.Message}");
        }
    }

    private static void EnsureWallpaperBehindDesktop(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            EnsureWindowBorderless(hwnd);
            TryDisableRoundedCorners(hwnd);
            // If top-level (fallback) and not already parented to WorkerW/Progman, parent to Progman so wallpaper sits behind desktop icons (SHELLDLL_DefView).
            try
            {
                var parent = GetParent(hwnd);
                if (parent == IntPtr.Zero)
                {
                    var progman = FindWindowW("Progman", null);
                    if (progman != IntPtr.Zero)
                    {
                        SetParent(hwnd, progman);
                        System.Diagnostics.Debug.WriteLine($"[App] EnsureWallpaperBehindDesktop SetParent to Progman=0x{progman.ToInt64():X} for fallback behind icons");
                    }
                }
            }
            catch { }
            ShowWindow(hwnd, 8); // SW_SHOWNA - show without activate
            // Push to bottom behind taskbar (Shell_TrayWnd is TOPMOST) and behind icons; keep borderless and no-round.
            try { SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE); } catch { }
            TryDisableRoundedCorners(hwnd);
            System.Diagnostics.Debug.WriteLine($"[App] EnsureWallpaperBehindDesktop SW_SHOWNA HWND_BOTTOM borderless hwnd=0x{hwnd.ToInt64():X} behind icons/taskbar");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] EnsureWallpaperBehindDesktop failed: {ex.Message}");
        }
    }
}

public sealed class HiddenWallpaperHostWindow : Window
{
    public Microsoft.UI.Xaml.Controls.Image WallpaperImage { get; }
    public Microsoft.UI.Xaml.Controls.Grid RootGrid { get; }

    public HiddenWallpaperHostWindow()
    {
        Title = "Osage Lagtrain Wallpaper";
        WallpaperImage = new Microsoft.UI.Xaml.Controls.Image
        {
            Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch,
            Opacity = 1.0
        };
        RootGrid = new Microsoft.UI.Xaml.Controls.Grid
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xB2, 0xB2, 0xB2))
        };
        // Image on top of grey idle background - frames cover grey when playing
        RootGrid.Children.Add(WallpaperImage);
        Content = RootGrid;
        Activated += OnActivated;
    }

    private void OnActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            App.EnsureWindowBorderless(hwnd);
        }
        catch { }
        Activated -= OnActivated;
    }

    public void EnsureBorderless()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            App.EnsureWindowBorderless(hwnd);
        }
        catch { }
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
