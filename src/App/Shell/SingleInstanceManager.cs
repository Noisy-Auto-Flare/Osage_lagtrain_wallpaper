using System.Runtime.InteropServices;

namespace OsageLagtrain.App.Shell;

/// <summary>
/// Single instance via named Mutex Global\OsageLagtrain-v1 fallback Local\ per-user if Global denied.
/// Second instance posts RegisterWindowMessage("OsageLagtrain_ShowSettings") via HWND_BROADCAST.
/// </summary>
public sealed class SingleInstanceManager : IDisposable
{
    public const string MutexNameGlobal = @"Global\OsageLagtrain-v1";
    public const string MutexNameLocal = @"Local\OsageLagtrain-v1";
    public const string ShowSettingsMessage = "OsageLagtrain_ShowSettings";

    private readonly IMutexFactory _mutexFactory;
    private readonly IWindowMessageInterop _msgInterop;

    private IMutexHandle? _mutex;
    private bool _isFirstInstance;
    private uint _showSettingsMsgId;
    private bool _disposed;

    public bool IsFirstInstance => _isFirstInstance;
    public uint ShowSettingsMessageId => _showSettingsMsgId;
    public string ActiveMutexName { get; private set; } = string.Empty;

    public SingleInstanceManager(IMutexFactory? mutexFactory = null, IWindowMessageInterop? msgInterop = null)
    {
        _mutexFactory = mutexFactory ?? new SystemMutexFactory();
        _msgInterop = msgInterop ?? new SystemWindowMessageInterop();
    }

    /// <summary>
    /// Try acquire mutex. Returns true if first instance.
    /// Falls back to Local if Global denied (UnauthorizedAccessException).
    /// </summary>
    public bool TryAcquire()
    {
        // Try Global first
        try
        {
            var handle = _mutexFactory.TryCreate(MutexNameGlobal, out bool createdNew);
            if (handle != null)
            {
                _mutex = handle;
                ActiveMutexName = MutexNameGlobal;
                _isFirstInstance = createdNew;
                if (_isFirstInstance)
                    _showSettingsMsgId = _msgInterop.RegisterWindowMessage(ShowSettingsMessage);
                return _isFirstInstance;
            }
        }
        catch (UnauthorizedAccessException)
        {
            // fallback to Local
        }
        catch (Exception)
        {
            // try fallback anyway
        }

        // Fallback Local
        try
        {
            var handle = _mutexFactory.TryCreate(MutexNameLocal, out bool createdNew);
            if (handle != null)
            {
                _mutex = handle;
                ActiveMutexName = MutexNameLocal;
                _isFirstInstance = createdNew;
                if (_isFirstInstance)
                    _showSettingsMsgId = _msgInterop.RegisterWindowMessage(ShowSettingsMessage);
                return _isFirstInstance;
            }
        }
        catch { }

        _isFirstInstance = false;
        return false;
    }

    /// <summary>
    /// Called by second instance to notify first instance via broadcast registered message.
    /// Must use RegisterWindowMessage + PostMessage HWND_BROADCAST (0xFFFF), not bare WM_USER.
    /// </summary>
    public bool NotifyFirstInstance()
    {
        try
        {
            uint msg = _msgInterop.RegisterWindowMessage(ShowSettingsMessage);
            // HWND_BROADCAST = 0xFFFF
            var hwndBroadcast = new IntPtr(0xFFFF);
            return _msgInterop.PostMessage(hwndBroadcast, msg, IntPtr.Zero, IntPtr.Zero);
        }
        catch { return false; }
    }

    /// <summary>
    /// First instance WndProc handler: if msg == registered ShowSettingsMessage, restore and show settings.
    /// Returns true if handled.
    /// </summary>
    public bool HandleWindowMessage(uint msg, Action? onShowSettings = null)
    {
        if (_showSettingsMsgId == 0)
            _showSettingsMsgId = _msgInterop.RegisterWindowMessage(ShowSettingsMessage);
        if (msg == _showSettingsMsgId)
        {
            try { onShowSettings?.Invoke(); } catch { }
            return true;
        }
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _mutex?.Dispose(); } catch { }
        _mutex = null;
    }
}

public interface IMutexHandle : IDisposable
{
    bool IsHeld { get; }
}

public interface IMutexFactory
{
    IMutexHandle? TryCreate(string name, out bool createdNew);
}

public interface IWindowMessageInterop
{
    uint RegisterWindowMessage(string name);
    bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}

internal sealed class SystemMutexFactory : IMutexFactory
{
    public IMutexHandle? TryCreate(string name, out bool createdNew)
    {
        var mutex = new Mutex(initiallyOwned: true, name: name, createdNew: out createdNew);
        // If not createdNew, we didn't get ownership but handle exists
        // Keep handle to allow dispose; second instance should dispose immediately and signal
        if (!createdNew)
        {
            // We created handle but not owner — still need to dispose
            // Return a wrapper that indicates not held
            return new SystemMutexHandle(mutex, isHeld: false, createdNew: false);
        }
        return new SystemMutexHandle(mutex, isHeld: true, createdNew: true);
    }

    private sealed class SystemMutexHandle : IMutexHandle
    {
        private readonly Mutex _mutex;
        public bool IsHeld { get; }
        public bool CreatedNew { get; }
        public SystemMutexHandle(Mutex m, bool isHeld, bool createdNew) { _mutex = m; IsHeld = isHeld; CreatedNew = createdNew; }
        public void Dispose()
        {
            try
            {
                if (IsHeld)
                {
                    try { _mutex.ReleaseMutex(); } catch { }
                }
                _mutex.Dispose();
            }
            catch { }
        }
    }
}

internal sealed class SystemWindowMessageInterop : IWindowMessageInterop
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessageW(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    public uint RegisterWindowMessage(string name) => RegisterWindowMessageW(name);
    public bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam) => PostMessageW(hwnd, msg, wParam, lParam);
}
