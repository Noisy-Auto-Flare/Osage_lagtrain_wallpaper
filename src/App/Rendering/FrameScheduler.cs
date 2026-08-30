namespace OsageLagtrain.App.Rendering;

/// <summary>
/// FPS timer + frame index calculation per T7 spec.
/// DispatcherTimer Interval = 1000/fps (not CompositionTarget.Rendering) to avoid 60Hz overdraw for ≤30fps B&W.
/// frameIndex = (elapsed * fps) % frames.Count
/// once: clamp at last + holdLastMs then idle (-1)
/// loop: modulo
/// pingpong: idx = pingpong(elapsed) off-by-default but implemented
/// </summary>
public enum PlayMode
{
    Once,
    Loop,
    PingPong
}

public static class FrameScheduler
{
    public const string MustUseDispatcherTimer = "Must use DispatcherTimer, not CompositionTarget.Rendering — see T7 spec";

    /// <summary>
    /// Timer interval for given fps. Used to assert 83ms for 12fps ±10ms.
    /// Must be DispatcherTimer Interval, not CompositionTarget.Rendering.
    /// </summary>
    public static TimeSpan GetInterval(int fps)
    {
        if (fps < 1 || fps > 30) throw new ArgumentOutOfRangeException(nameof(fps), "fps must be 1..30");
        double ms = 1000.0 / fps;
        return TimeSpan.FromMilliseconds(ms);
    }

    /// <summary>
    /// Calculate frame index for elapsed time.
    /// Returns -1 when in idle after once+holdLastMs (caller should show idle #b2b2b2).
    /// </summary>
    public static int GetFrameIndex(TimeSpan elapsed, int fps, int frameCount, PlayMode mode, int holdLastMs = 0)
    {
        if (frameCount <= 0) return -1;
        if (fps < 1 || fps > 30) throw new ArgumentOutOfRangeException(nameof(fps));
        if (holdLastMs < 0 || holdLastMs > 5000) throw new ArgumentOutOfRangeException(nameof(holdLastMs));

        double elapsedSec = elapsed.TotalSeconds;
        double totalFramesElapsed = elapsedSec * fps;
        // epsilon to cure TimeSpan.FromSeconds(1.0/12) => 0.9999996 -> floor 1
        long totalFramesElapsedInt = (long)Math.Floor(totalFramesElapsed + 1e-6);

        switch (mode)
        {
            case PlayMode.Loop:
                // modulo: frameIndex = (elapsed * fps) % frames.Count — with epsilon for tick rounding
                return (int)(totalFramesElapsedInt % frameCount);

            case PlayMode.Once:
            {
                double sceneDurationSec = (double)frameCount / fps;
                double holdSec = holdLastMs / 1000.0;
                double endSec = sceneDurationSec + holdSec;
                if (elapsedSec >= endSec)
                    return -1;
                if (totalFramesElapsedInt >= frameCount)
                    return frameCount - 1;
                return (int)totalFramesElapsedInt;
            }

            case PlayMode.PingPong:
                return PingPongIndex(elapsedSec, fps, frameCount);

            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    /// <summary>
    /// Pingpong index off-by-default but implemented per spec:
    /// idx = pingpong(elapsed) where period = 2*(count-1) frames.
    /// 0,1,2,...,n-1,n-2,...,1,0,1...
    /// </summary>
    public static int PingPongIndex(double elapsedSec, int fps, int frameCount)
    {
        if (frameCount <= 0) return -1;
        if (frameCount == 1) return 0;
        int period = 2 * (frameCount - 1);
        long total = (long)(elapsedSec * fps + 0.0001); // tiny epsilon for float
        int pos = (int)(total % period);
        if (pos < frameCount) return pos;
        return period - pos;
    }

    /// <summary>
    /// Helper: get SceneMode string from Cycle model to PlayMode.
    /// </summary>
    public static PlayMode FromSceneMode(Cycles.SceneConfig config)
    {
        if (config.Mode is Cycles.SceneMode.StringMode sm)
        {
            return sm.Value switch
            {
                "once" => PlayMode.Once,
                "loop" => PlayMode.Loop,
                "pingpong" => PlayMode.PingPong,
                _ => PlayMode.Once
            };
        }
        if (config.Mode is Cycles.SceneMode.CountMode)
            return PlayMode.Loop; // {count: N} treated as loop N times — caller handles count limit externally
        return PlayMode.Once;
    }

    /// <summary>
    /// For once with count limit: if mode is {count:N}, total play is N loops (not infinite).
    /// Returns -1 after N*frameCount/fps + hold.
    /// </summary>
    public static int GetFrameIndexWithCount(TimeSpan elapsed, int fps, int frameCount, Cycles.SceneConfig config)
    {
        if (config.Mode is Cycles.SceneMode.CountMode cm)
        {
            double loopDuration = (double)frameCount / fps;
            double totalSec = loopDuration * cm.Count + config.HoldLastMs / 1000.0;
            if (elapsed.TotalSeconds >= totalSec) return -1;
            if (elapsed.TotalSeconds >= loopDuration * cm.Count) return frameCount - 1;
            long totalInt = elapsed.Ticks * fps / TimeSpan.TicksPerSecond;
            return (int)(totalInt % frameCount);
        }
        var mode = FromSceneMode(config);
        return GetFrameIndex(elapsed, fps, frameCount, mode, config.HoldLastMs);
    }
}
