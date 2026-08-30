using Microsoft.Win32;

namespace OsageLagtrain.App.Shell;

/// <summary>
/// HKCU Run autostart manager. Uses HKCU\Software\Microsoft\Windows\CurrentVersion\Run value OsageLagtrain.
/// Uses per-user hive only, no machine hive, no common app data, no service.
/// </summary>
public sealed class AutostartManager
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "OsageLagtrain";

    private readonly IRegistryProvider _provider;
    private readonly Func<string> _exePathProvider;

    public AutostartManager(IRegistryProvider? provider = null, Func<string>? exePathProvider = null)
    {
        _provider = provider ?? new SystemRegistryProvider();
        _exePathProvider = exePathProvider ?? GetDefaultExePath;
    }

    private static string GetDefaultExePath()
    {
        try
        {
            var p = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(p)) return p;
        }
        catch { }
        try
        {
            var loc = typeof(AutostartManager).Assembly.Location;
            if (!string.IsNullOrEmpty(loc)) return loc;
        }
        catch { }
        return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar) + ".exe";
    }

    private static string Quote(string path) => "\"" + path + "\"";

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = _provider.OpenRunKey(writable: false);
                if (key == null) return false;
                var val = key.GetValue(ValueName) as string;
                if (string.IsNullOrEmpty(val)) return false;
                var exe = _exePathProvider();
                var quoted = Quote(exe);
                // Live check: value should contain quoted exe path
                return string.Equals(val, quoted, StringComparison.OrdinalIgnoreCase) || val.Contains(exe, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            using var key = _provider.OpenRunKey(writable: true);
            if (key == null) return;
            if (enabled)
            {
                var exe = _exePathProvider();
                var quoted = Quote(exe);
                key.SetValue(ValueName, quoted, RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissing: false);
            }
        }
        catch { }
    }

    public void Enable() => SetEnabled(true);
    public void Disable() => SetEnabled(false);
}

public interface IRegistryKey : IDisposable
{
    object? GetValue(string name);
    void SetValue(string name, object value, RegistryValueKind kind);
    void DeleteValue(string name, bool throwOnMissing);
}

public interface IRegistryProvider
{
    IRegistryKey? OpenRunKey(bool writable);
}

internal sealed class SystemRegistryProvider : IRegistryProvider
{
    public IRegistryKey? OpenRunKey(bool writable)
    {
        try
        {
            // Must use current-user hive
            var key = Registry.CurrentUser.CreateSubKey(AutostartManager.RunKeyPath, writable);
            if (key == null) return null;
            return new SystemRegistryKey(key);
        }
        catch { return null; }
    }

    private sealed class SystemRegistryKey : IRegistryKey
    {
        private readonly RegistryKey _key;
        public SystemRegistryKey(RegistryKey key) => _key = key;
        public object? GetValue(string name) => _key.GetValue(name);
        public void SetValue(string name, object value, RegistryValueKind kind) => _key.SetValue(name, value, kind);
        public void DeleteValue(string name, bool throwOnMissing) => _key.DeleteValue(name, throwOnMissing);
        public void Dispose() => _key.Dispose();
    }
}

// In-memory provider for tests
public sealed class InMemoryRegistryProvider : IRegistryProvider
{
    private readonly Dictionary<string, object> _store = new(StringComparer.OrdinalIgnoreCase);
    public IDictionary<string, object> Store => _store;
    public IReadOnlyDictionary<string, object> ReadStore => _store;

    public IRegistryKey? OpenRunKey(bool writable) => new InMemoryRegistryKey(_store, writable);

    private sealed class InMemoryRegistryKey : IRegistryKey
    {
        private readonly Dictionary<string, object> _store;
        private readonly bool _writable;
        public InMemoryRegistryKey(Dictionary<string, object> store, bool writable) { _store = store; _writable = writable; }
        public object? GetValue(string name) => _store.TryGetValue(name, out var v) ? v : null;
        public void SetValue(string name, object value, RegistryValueKind kind)
        {
            if (!_writable) throw new InvalidOperationException("read-only");
            _store[name] = value;
        }
        public void DeleteValue(string name, bool throwOnMissing)
        {
            if (!_writable) throw new InvalidOperationException("read-only");
            if (!_store.Remove(name) && throwOnMissing) throw new ArgumentException("value not found");
        }
        public void Dispose() { }
    }
}
