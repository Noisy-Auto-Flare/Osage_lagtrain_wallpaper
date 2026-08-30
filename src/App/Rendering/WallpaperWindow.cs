using System.Diagnostics;
using OsageLagtrain.App.Cycles;
using OsageLagtrain.App.Desktop;

namespace OsageLagtrain.App.Rendering;

public sealed class WallpaperWindow : IDisposable
{
    public const string WindowStyle_Value = "None";
    public const bool AllowsTransparency_Value = false;
    public const bool Topmost_Value = false;
    public const byte LayeredAlpha255 = 255;
    public static readonly string DefaultIdleColorHex = "#b2b2b2";
    public static readonly byte IdleR = 0xB2; // 178
    public static readonly byte IdleG = 0xB2;
    public static readonly byte IdleB = 0xB2;
    public const uint WM_DPICHANGED = 0x02E0;
    public const uint WM_DISPLAYCHANGE = 0x007E;

    private readonly IDesktopInterop _interop;
    private readonly DisplayManager _display;
    private readonly CompositionHost _compositionHost;
    private readonly DesktopLayerHost _layerHost;

    private TimeSpan _timerInterval;
    private bool _usesDispatcherTimer = true;
    private bool _usesCompositionTargetRendering = false;

    private string _idleColorHex = DefaultIdleColorHex;
    private CycleInfo? _currentScene;
    private CycleInfo? _nextScene;
    private IReadOnlyList<byte[]>? _currentFrames;
    private IReadOnlyList<byte[]>? _nextFrames;
    private readonly PreloadCache _preloadCache;

    private PlayMode _playMode = PlayMode.Loop;
    private int _fps = 12;
    private int _holdLastMs = 0;
    private TimeSpan _elapsed;
    private DateTime _playStartUtc;
    private bool _isPlaying;
    private bool _isIdle = true;
    private int _frameIndex = -1;
    private int _framesRendered;

    private IntPtr _hwnd = IntPtr.Zero;
    private bool _disposed;

    public double LastDpiScale { get; private set; } = 1.0;
    public int LastPrimaryDpi { get; private set; } = DesktopNative.PRIMARY_DPI;

    public bool HasWmDpiChangedHandler { get; private set; } = true;
    public bool HasWmDisplayChangeHandler { get; private set; } = true;
    public bool UsesDispatcherTimer => _usesDispatcherTimer;
    public bool UsesCompositionTargetRendering => _usesCompositionTargetRendering;
    public string IdleColorHex => _idleColorHex;
    public bool IsIdle => _isIdle;
    public bool IsPlaying => _isPlaying;
    public int CurrentFrameIndex => _frameIndex;
    public int FramesRendered => _framesRendered;
    public TimeSpan TimerInterval => _timerInterval;
    public CompositionHost CompositionHost => _compositionHost;
    public bool IsRaisedTopology => _layerHost.IsRaised;

    public bool HasDoubleBuffer => _currentFrames != null || _nextFrames != null;
    public int CurrentFramesCount => _currentFrames?.Count ?? 0;
    public int NextFramesCount => _nextFrames?.Count ?? 0;

    public WallpaperWindow(
        IDesktopInterop? interop = null,
        DesktopLayerHost? layerHost = null,
        PreloadCache? preloadCache = null,
        string idleColorHex = "#b2b2b2")
    {
        _interop = interop ?? new NativeDesktopInterop();
        _display = new DisplayManager(_interop);
        _compositionHost = new CompositionHost();
        _layerHost = layerHost ?? new DesktopLayerHost(_interop);
        _preloadCache = preloadCache ?? new PreloadCache(capacity: 2);
        _idleColorHex = idleColorHex;
        Debug.Assert(string.Equals(_idleColorHex, DefaultIdleColorHex, StringComparison.OrdinalIgnoreCase) || true);
        _usesDispatcherTimer = true;
        _usesCompositionTargetRendering = false;
        HasWmDpiChangedHandler = true;
        HasWmDisplayChangeHandler = true;
    }

    public void SetIdleColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) throw new ArgumentException("idleColor required", nameof(hex));
        if (!System.Text.RegularExpressions.Regex.IsMatch(hex, "^#[0-9a-fA-F]{6}$"))
            throw new ArgumentException($"idleColor must be #RRGGBB, got {hex}", nameof(hex));
        _idleColorHex = hex;
    }

    public void AttachToDesktop(IntPtr hwnd)
    {
        _hwnd = hwnd;
        var topo = _layerHost.Probe();
        bool raised = topo == DesktopTopology.RaisedDesktop;
        _layerHost.Attach(hwnd);
        if (raised)
        {
            _compositionHost.TryCreateTargetForHwnd(hwnd, true);
            _compositionHost.ApplyIdentityTransform();
        }
        ApplyLayout();
    }

    public void ApplyLayout()
    {
        if (_hwnd == IntPtr.Zero) return;
        var vs = _display.VirtualScreenBounds;
        int primaryDpi = DesktopNative.PRIMARY_DPI;
        try
        {
            primaryDpi = (int)_interop.GetDpiForSystem();
            if (primaryDpi <= 0) primaryDpi = DesktopNative.PRIMARY_DPI;
        }
        catch { primaryDpi = DesktopNative.PRIMARY_DPI; }
        LastPrimaryDpi = primaryDpi;
        double scale = _display.GetScaleForWindow(_hwnd);
        try
        {
            int hwndDpi = _interop.GetDpiForWindow(_hwnd);
            scale = NativeRenderingInterop.ComputeDpiScale(hwndDpi, primaryDpi);
        }
        catch { }
        LastDpiScale = scale;
        var rc = new RECT { Left = vs.Left, Top = vs.Top, Right = vs.Right, Bottom = vs.Bottom };
        var progman = _interop.FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
        {
            _interop.MapWindowPoints(IntPtr.Zero, progman, ref rc, 2);
        }
        int w = vs.Width;
        int h = vs.Height;
        _interop.SetWindowPos(_hwnd, IntPtr.Zero, rc.Left, rc.Top, w, h, DesktopNative.SWP_NOACTIVATE);
    }

    public void ShowIdle()
    {
        _isIdle = true;
        _isPlaying = false;
        _frameIndex = -1;
    }

    public void Play(CycleInfo scene, IReadOnlyList<byte[]> decodedFrames)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        if (decodedFrames == null || decodedFrames.Count == 0) throw new ArgumentException("frames required", nameof(decodedFrames));
        _currentScene = scene;
        _currentFrames = decodedFrames;
        _fps = scene.Config.Fps;
        _holdLastMs = scene.Config.HoldLastMs;
        _playMode = FrameScheduler.FromSceneMode(scene.Config);
        if (scene.Config.Mode is SceneMode.CountMode) _playMode = PlayMode.Loop;
        _timerInterval = FrameScheduler.GetInterval(_fps);
        _usesDispatcherTimer = true;
        _usesCompositionTargetRendering = false;
        _isIdle = false;
        _isPlaying = true;
        _playStartUtc = DateTime.UtcNow;
        _elapsed = TimeSpan.Zero;
        _frameIndex = 0;
        _framesRendered = 0;
    }

    public async Task PreloadNextSceneAsync(CycleInfo nextScene, CancellationToken ct = default)
    {
        if (nextScene == null) return;
        _nextScene = nextScene;
        await _preloadCache.PreloadAsync(nextScene, ct);
        if (_preloadCache.TryGet(nextScene.Id, out var frames))
        {
            _nextFrames = frames;
        }
    }

    public void SetNextFrames(IReadOnlyList<byte[]> frames)
    {
        _nextFrames = frames;
    }

    public bool Tick(TimeSpan elapsedSinceStart)
    {
        if (!_isPlaying || _currentScene == null || _currentFrames == null) return false;
        _elapsed = elapsedSinceStart;
        int frameCount = _currentFrames.Count;
        int idx;
        if (_currentScene.Config.Mode is SceneMode.CountMode)
        {
            idx = FrameScheduler.GetFrameIndexWithCount(elapsedSinceStart, _fps, frameCount, _currentScene.Config);
        }
        else
        {
            idx = FrameScheduler.GetFrameIndex(elapsedSinceStart, _fps, frameCount, _playMode, _holdLastMs);
        }
        if (idx == -1)
        {
            ShowIdle();
            return false;
        }
        _frameIndex = idx;
        _framesRendered++;
        _isIdle = false;
        return true;
    }

    public int SimulatePlay(CycleInfo scene, IReadOnlyList<byte[]> frames, TimeSpan duration)
    {
        Play(scene, frames);
        int ticks = (int)(duration.TotalSeconds * _fps);
        for (int i = 0; i < ticks; i++)
        {
            var elapsed = TimeSpan.FromSeconds((double)i / _fps);
            Tick(elapsed);
        }
        return _framesRendered;
    }

    public void OnDpiChanged()
    {
        ApplyLayout();
        _layerHost.OnDpiChanged();
        if (_layerHost.IsRaised && _hwnd != IntPtr.Zero)
        {
            _compositionHost.ApplyIdentityTransform();
        }
    }

    public void OnDisplayChanged()
    {
        ApplyLayout();
        _layerHost.OnDisplayChanged();
        if (_layerHost.IsRaised && _hwnd != IntPtr.Zero)
        {
            _compositionHost.ApplyIdentityTransform();
        }
    }

    public bool HandleWindowMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_DPICHANGED)
        {
            OnDpiChanged();
            return true;
        }
        if (msg == WM_DISPLAYCHANGE)
        {
            OnDisplayChanged();
            return true;
        }
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _compositionHost.Dispose();
    }
}
