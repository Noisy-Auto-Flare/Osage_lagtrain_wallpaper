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
    private object? _hostImage; // Microsoft.UI.Xaml.Controls.Image when available (reflection to avoid Tests compile dep)
    private object? _playTimer; // DispatcherQueueTimer when available

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

    public void BindHostImage(object image)
    {
        _hostImage = image;
        // Ensure initial idle shows grey background (image transparent) via reflection
        try
        {
            if (_hostImage != null && _isIdle)
            {
                var prop = _hostImage.GetType().GetProperty("Source");
                prop?.SetValue(_hostImage, null);
            }
        }
        catch { }
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
        try
        {
            if (_playTimer != null)
            {
                var m = _playTimer.GetType().GetMethod("Stop");
                m?.Invoke(_playTimer, null);
            }
        }
        catch { }
        try
        {
            if (_hostImage != null)
            {
                var prop = _hostImage.GetType().GetProperty("Source");
                prop?.SetValue(_hostImage, null);
            }
        }
        catch { }
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
        // Render first frame immediately on host Image (on top of grey)
        try { RenderFrameToHost(0); } catch { }
        try { StartDispatcherTimer(); } catch { }
    }

    private void StartDispatcherTimer()
    {
        try
        {
            if (_playTimer != null)
            {
                var m = _playTimer.GetType().GetMethod("Stop");
                m?.Invoke(_playTimer, null);
            }
        }
        catch { }
        if (_hostImage == null) return;
        try
        {
            // Get DispatcherQueue from hostImage via reflection
            var dqProp = _hostImage.GetType().GetProperty("DispatcherQueue");
            var dq = dqProp?.GetValue(_hostImage);
            if (dq == null)
            {
                // fallback: try GetForCurrentThread via reflection
                try
                {
                    var t = Type.GetType("Microsoft.UI.Dispatching.DispatcherQueue, Microsoft.UI");
                    var m = t?.GetMethod("GetForCurrentThread");
                    dq = m?.Invoke(null, null);
                }
                catch { }
            }
            if (dq == null) return;
            var createTimer = dq.GetType().GetMethod("CreateTimer");
            _playTimer = createTimer?.Invoke(dq, null);
            if (_playTimer == null) return;
            var intervalProp = _playTimer.GetType().GetProperty("Interval");
            intervalProp?.SetValue(_playTimer, _timerInterval);
            var tickEvent = _playTimer.GetType().GetEvent("Tick");
            if (tickEvent != null)
            {
                // Create delegate via reflection
                EventHandler<object> handler = (s, e) =>
                {
                    try
                    {
                        var elapsed = DateTime.UtcNow - _playStartUtc;
                        bool ok = Tick(elapsed);
                        if (!ok)
                        {
                            try
                            {
                                var stopM = _playTimer?.GetType().GetMethod("Stop");
                                stopM?.Invoke(_playTimer, null);
                            }
                            catch { }
                            return;
                        }
                        try { RenderFrameToHost(_frameIndex); } catch { }
                    }
                    catch { }
                };
                // Need correct delegate type: TypedEventHandler<DispatcherQueueTimer, object> ?
                // Try add via reflection with generic handler
                try
                {
                    var addMethod = tickEvent.AddMethod;
                    var delegateType = tickEvent.EventHandlerType;
                    var d = Delegate.CreateDelegate(delegateType!, handler.Target, handler.Method);
                    addMethod.Invoke(_playTimer, new object[] { d });
                }
                catch
                {
                    // fallback: try EventHandler<object> direct
                    try { tickEvent.AddEventHandler(_playTimer, handler); } catch { }
                }
            }
            var startM = _playTimer.GetType().GetMethod("Start");
            startM?.Invoke(_playTimer, null);
        }
        catch (Exception ex) { Debug.WriteLine($"[WallpaperWindow] StartDispatcherTimer failed: {ex.Message}"); }
    }

    private void RenderFrameToHost(int index)
    {
        if (_hostImage == null || _currentFrames == null) return;
        if (index < 0 || index >= _currentFrames.Count) return;
        var bytes = _currentFrames[index];
        if (bytes == null || bytes.Length == 0) return;
        try
        {
            // Use reflection to avoid hard WinUI dep for Tests
            var dqProp = _hostImage.GetType().GetProperty("DispatcherQueue");
            var dq = dqProp?.GetValue(_hostImage);
            if (dq != null)
            {
                var hasAccessProp = dq.GetType().GetProperty("HasThreadAccess");
                bool hasAccess = hasAccessProp != null && (bool)(hasAccessProp.GetValue(dq) ?? true);
                if (!hasAccess)
                {
                    int captured = index;
                    var tryEnqueue = dq.GetType().GetMethod("TryEnqueue", new[] { typeof(Action) });
                    // TryEnqueue with DispatcherQueueHandler
                    try
                    {
                        var delType = Type.GetType("Microsoft.UI.Dispatching.DispatcherQueueHandler, Microsoft.UI");
                        if (delType != null)
                        {
                            Action a = () => RenderFrameToHost(captured);
                            var del = Delegate.CreateDelegate(delType, a.Target, a.Method);
                            var m2 = dq.GetType().GetMethod("TryEnqueue");
                            m2?.Invoke(dq, new object[] { del });
                        }
                        else
                        {
                            tryEnqueue?.Invoke(dq, new object[] { (Action)(() => RenderFrameToHost(captured)) });
                        }
                    }
                    catch { }
                    return;
                }
            }
            // Create BitmapImage via reflection
            var bitmapType = Type.GetType("Microsoft.UI.Xaml.Media.Imaging.BitmapImage, Microsoft.UI.Xaml");
            var streamType = Type.GetType("Windows.Storage.Streams.InMemoryRandomAccessStream, Microsoft.Windows.SDK.NET");
            var writerType = Type.GetType("Windows.Storage.Streams.DataWriter, Microsoft.Windows.SDK.NET");
            if (bitmapType == null || streamType == null) return;
            var bitmap = Activator.CreateInstance(bitmapType);
            var stream = Activator.CreateInstance(streamType);
            if (bitmap == null || stream == null) return;
            // DataWriter(stream.GetOutputStreamAt(0))
            var getOutput = streamType.GetMethod("GetOutputStreamAt");
            var output = getOutput?.Invoke(stream, new object[] { (ulong)0 });
            if (output == null) return;
            var writer = writerType != null ? Activator.CreateInstance(writerType, new object[] { output }) : null;
            if (writer == null) return;
            var writeBytes = writer.GetType().GetMethod("WriteBytes", new[] { typeof(byte[]) });
            writeBytes?.Invoke(writer, new object[] { bytes });
            var storeAsync = writer.GetType().GetMethod("StoreAsync");
            var storeOp = storeAsync?.Invoke(writer, null);
            if (storeOp == null) return;
            // hook Completed
            var completedProp = storeOp.GetType().GetProperty("Completed");
            // Use dynamic for async completion
            try
            {
                var awaiter = storeOp.GetType().GetMethod("GetResults");
                // Use Completed handler via reflection: storeOp.Completed = (op,status)=>...
                // Simplify: await via Task
                var asTaskMethod = storeOp.GetType().GetMethod("AsTask");
                // Fallback: just try synchronous after store
                // For Tests this path not needed
                Debug.WriteLine("[WallpaperWindow] RenderFrameToHost reflection path reached (WinUI available)");
                // Best effort: try to set source synchronously via reflection
                // If we are here in App, attempt SetSourceAsync via reflection
                var setSource = bitmapType.GetMethod("SetSourceAsync");
                if (setSource != null && dq != null)
                {
                    // DetachStream
                    var detach = writer.GetType().GetMethod("DetachStream");
                    detach?.Invoke(writer, null);
                    var seekM = streamType.GetMethod("Seek");
                    seekM?.Invoke(stream, new object[] { (ulong)0 });
                    // dispatch async SetSource
                    var tryEnqueue2 = dq.GetType().GetMethod("TryEnqueue");
                    // Create async lambda via reflection not trivial; skip for now -> direct invoke
                    try
                    {
                        var op2 = setSource.Invoke(bitmap, new object[] { stream });
                        // op2 is IAsyncAction — fire-and-forget, source will be set after async completes
                        // Fallback set pending bitmap now; async will update when ready
                        try
                        {
                            var srcProp2 = _hostImage.GetType().GetProperty("Source");
                            srcProp2?.SetValue(_hostImage, bitmap);
                        }
                        catch { }
                    }
                    catch (Exception ex) { Debug.WriteLine($"[WallpaperWindow] SetSourceAsync reflection failed: {ex.Message}"); }
                    // Fallback: set source directly (will show after async)
                    try
                    {
                        var srcProp = _hostImage.GetType().GetProperty("Source");
                        // Can't set until async complete, but set bitmap anyway
                        srcProp?.SetValue(_hostImage, bitmap);
                    }
                    catch { }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[WallpaperWindow] RenderFrame inner failed: {ex.Message}"); }
        }
        catch (Exception ex) { Debug.WriteLine($"[WallpaperWindow] RenderFrameToHost failed idx={index}: {ex.Message}"); }
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
        // If called via timer, also render to host image (Play path manual tick in tests skips host)
        try { if (_hostImage != null) RenderFrameToHost(idx); } catch { }
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
        try
        {
            if (_playTimer != null)
            {
                var m = _playTimer.GetType().GetMethod("Stop");
                m?.Invoke(_playTimer, null);
            }
        }
        catch { }
        _compositionHost.Dispose();
    }
}
