using OsageLagtrain.Tests.E2E;
using Xunit;

namespace OsageLagtrain.Tests;

/// <summary>
/// E2E harness tests — each fact maps to one of 9 scenarios plus matrix/budget/history.
/// Tagged so `dotnet test --filter E2E` passes. Uses QAHarness mocks (DesktopLayerHost, WindowMonitor, etc.)
/// </summary>
[Trait("Category", "E2E")]
public sealed class E2ETests
{
    [Fact]
    public void E2E_Probe_RaisedVsClassic()
    {
        var h = new QAHarness();
        Assert.True(h.Scenario_Probe_RaisedVsClassic());
    }

    [Fact]
    public void E2E_IsCovered_95_DwmVsGetWindowRect()
    {
        var h = new QAHarness();
        Assert.True(h.Scenario_IsCovered_95());
    }

    [Fact]
    public void E2E_SHQuery_D3D()
    {
        var h = new QAHarness();
        Assert.True(h.Scenario_SHQuery_D3D());
    }

    [Fact]
    public void E2E_PostEventDelayMs_500_Jitter()
    {
        var h = new QAHarness();
        Assert.True(h.Scenario_PostEventDelayMs());
    }

    [Fact]
    public void E2E_RandomNoRepeat_N3_100Picks()
    {
        var h = new QAHarness();
        Assert.True(h.Scenario_RandomNoRepeat());
    }

    [Fact]
    public void E2E_MemoryCpu_Budgets()
    {
        var h = new QAHarness();
        Assert.True(h.Scenario_MemoryCpuBudgets());
    }

    [Fact]
    public void E2E_WM_DPICHANGED()
    {
        var h = new QAHarness();
        Assert.True(h.Scenario_WmDpiChanged());
    }

    [Fact]
    public void E2E_ExplorerRestart_Heal_LT2s()
    {
        var h = new QAHarness();
        Assert.True(h.Scenario_ExplorerRestartHeal());
    }

    [Fact]
    public void E2E_HDR_OnOff_MitigatedViaDComp()
    {
        var h = new QAHarness();
        Assert.True(h.Scenario_HDR());
    }

    [Fact]
    public void E2E_History_1KB_Cap_LeakDetection()
    {
        var h = new QAHarness();
        Assert.True(h.Scenario_History_1KB_Leak());
    }

    [Fact]
    public void E2E_QAMatrix_100_150_200_x1_2_x_HDR_x_Restart()
    {
        var h = new QAHarness();
        var rows = h.GenerateMatrix();
        Assert.Equal(10, rows.Count);
        // Verify coverage of required combos
        Assert.Contains(rows, r => r.Dpi == "100%" && r.Monitors == 1);
        Assert.Contains(rows, r => r.Dpi == "100%" && r.Monitors == 2);
        Assert.Contains(rows, r => r.Dpi == "150%" && r.Monitors == 1 && r.ExplorerRestart == "yes");
        Assert.Contains(rows, r => r.Dpi == "150%" && r.Hdr == "on");
        Assert.Contains(rows, r => r.Dpi == "200%" && r.Monitors == 2);
        Assert.Contains(rows, r => r.Quns == "yes");
        Assert.Contains(rows, r => r.Borderless == "yes");
    }

    [Fact]
    public void E2E_Harness_RunAll_And_WriteEvidence()
    {
        var h = new QAHarness();
        bool all = h.RunAllScenarios();
        Assert.True(all, h.EvidenceText);

        // Write evidence to .omo/evidence/task-13-osage-lagtrain-wallpaper.log|md
        var repoRoot = RepoRoot();
        bool wrote = h.WriteEvidence(repoRoot);
        Assert.True(wrote);
        Assert.True(File.Exists(Path.Combine(repoRoot, ".omo", "evidence", "task-13-osage-lagtrain-wallpaper.log")));
        Assert.True(File.Exists(Path.Combine(repoRoot, ".omo", "evidence", "task-13-osage-lagtrain-wallpaper.md")));
    }

    private static string RepoRoot()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(baseDir, "..","..","..","..","..")),
            Path.GetFullPath(Path.Combine(baseDir, "..","..","..","..")),
            @"G:\Projects\Osage_lagtrain_wallpaper"
        };
        foreach(var c in candidates) if(Directory.Exists(Path.Combine(c, ".omo"))) return c;
        return candidates.Last();
    }
}
