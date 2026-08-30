# QA Matrix , Osage Lagtrain Wallpaper

Manual QA checklist. Each cell is Pass, Fail, or N/A with notes. Test on Windows 11 22H2 or newer, ideally 24H2 for raised desktop.

## Legend

* **Pass** , wallpaper visible, correctly scaled, advances on covering window close, heals after trigger
* **Fail** , bare area, wrong z order, no advance, QUNS pause stuck, crash
* **N/A** , not applicable (for example single monitor column when testing 2 monitors)

---

## 1. DPI and monitor count

| DPI | 1 monitor | 2 monitors (extend) |
|-----|-----------|---------------------|
| **100%** | Pass: probe Classic or Raised, span covers virtual screen, 95% coverage triggers. Check `GetDpiForWindow` scale 1.0, `MapWindowPoints` not `0,0`. | Pass: `EnumerateMonitors` 2 entries, per monitor `SetWindowPos` per `rcMonitor`, span optional. Verify both monitors show wallpaper, no duplication offset. |
| **150%** | Pass: PerMonitorV2 plus DComp identity `1:1` , no 55% bare. `CompositionHost.HasIdentityTransform=true`, `CreateTargetForHwnd(hwnd,true)` used on raised. Fallback WriteableBitmap also scaled via `DpiScale`. | Pass: each monitor scaled independently. Raised: `MapWindowPoints(0,Progman)` plus per monitor `DpiScale`. Check primary 150%, secondary 100% split if mixed DPI. |
| **200%** | Pass: same as 150%, verify `w = bounds.Width * scale` not clipped. | Pass: verify no blur, physical pixels 1:1. HDR off for this row, on in HDR section. |

Steps per cell:

1. Set Settings then System then Display then Scale to target
2. Restart app or toggle Enable off then on
3. Check wallpaper covers full screen, no bare strip at edge
4. Maximize Notepad, then close or minimize, desktop foreground should advance once after `postEventDelayMs` 500 ms

---

## 2. Topology: Classic vs Raised (24H2)

| Scenario | Expected |
|----------|----------|
| **Classic WorkerW** (22H2 or 24H2 with WS_EX_NOREDIRECTIONBITMAP clear) | `Probe()` returns `ClassicWorkerW`, `AttachClassic` parents to `WorkerW`, `EnsureWorkerWZOrder` pushes `WorkerW` to `HWND_BOTTOM`, wallpaper under icons but above desktop color. |
| **Raised Desktop** (24H2 with WS_EX_NOREDIRECTIONBITMAP set) | `Probe()` returns `RaisedDesktop`, `AttachRaised` parents to `Progman`, inserts under `SHELLDLL_DefView` with `SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE`, never `HWND_BOTTOM`. Verify no 0,0 literal, `MapWindowPoints` used. |
| **Explorer restart** | Kill `explorer.exe` then start it. Healing via `TaskbarCreated` plus `EVENT_OBJECT_DESTROY` plus 20 times 300 ms retry must re probe and re attach. Log `Healing: re-probe success`. |

Probe logic: `FindWindow("Progman")` each time, `GetWindowLongPtr(GWL_EXSTYLE) & WS_EX_NOREDIRECTIONBITMAP != 0`. Never cache Progman HWND.

---

## 3. HDR

| HDR off | HDR on |
|---------|--------|
| Pass: colors correct, DComp not required for color. | Pass or known limit: DComp mitigates wash on raised. v1 has no HDR color management. If washed, note and try disabling HDR for desktop. Check `idleColor #b2b2b2` fallback. |

Test: Settings then System then Display then HDR, toggle, restart app.

---

## 4. Window monitoring , covering and debounce

| Case | Expected |
|------|----------|
| **Maximized Notepad closes to desktop** | Advance fires once after 500 ms debounce plus `postEventDelayMs`. `IsZoomed` or `DWMWA_EXTENDED_FRAME_BOUNDS` vs `rcMonitor` 95% passes. |
| **VS Code borderless maximized** | Advance fires once (borderless still covers at 95% via `rcWork`). Verify both `rcMonitor` and `rcWork` checked. |
| **Small window Alt Tab to desktop** | No advance. Small window fails `CoversMonitor` (area below 95%), `previousWasCovering` stays false. |
| **Debounce 500 ms spam** | Rapid foreground changes coalesced to one `EvaluateCovering` per 500 ms. Check `debounceTimer` single fire. |
| **Interval 0 DComp tick** | `CompositionTarget.Rendering` skipped when `IsPaused`. |

Per scene override: `scene.json postEventDelayMs:1200` should delay that scene 1200 ms, global 500 ms otherwise.

---

## 5. QUNS D3D fullscreen pause

| Case | Expected |
|------|----------|
| **Exclusive fullscreen game** | `SHQueryUserNotificationState` returns `QUNS_RUNNING_D3D_FULL_SCREEN` (3 or compat 7), `IsPausedByD3D=true`, no advance while game is foreground. Cache 500 ms, `ShQueryCalls` increments at most once per 500 ms. |
| **Borderless D3D fullscreen** | Same pause if QUNS reports D3D. Verify pause, then resume after Alt Tab to desktop. |
| **Game exit to desktop** | After QUNS returns to `QUNS_ACCEPTS_NOTIFICATIONS`, `IsPausedByD3D=false`, next covering close advances normally. If stuck, wait 600 ms and refocus desktop. |

Note: QUNS nuance is caching 500 ms plus handling both values 3 and 7 as D3D. Do not poll `SHQueryUserNotificationState` on every event without cache.

---

## 6. Explorer restart and healing

| Trigger | Expected |
|---------|----------|
| **WM_TASKBARCREATED** (`RegisterWindowMessage`) | Re probe, re attach, wallpaper returns. |
| **EVENT_OBJECT_DESTROY** (`SetWinEventHook` on Progman pid) | Same healing. |
| **WM_DISPLAYCHANGE** (resolution change) | Re probe with retry, `VirtualScreenBounds` updated. |
| **WM_DPICHANGED** (per monitor DPI change) | Re probe, `GetScaleForWindow` updated, no bare area. |
| **WTS_SESSION_UNLOCK** | Re probe, wallpaper visible after unlock. `PauseForSession` cleared. |

Healing retry: 20 times 300 ms `EnsureLayer` loop. Verify in logs.

---

## 7. Selection policy

| Policy | Expected |
|--------|----------|
| **randomNoRepeat N=3** (default) | Last 3 scenes not repeated until pool exhausted. `history.json recent` truncated to 3, 1 KB cap. `window 0` means no dedup. |
| **randomPure** | Uniform random, may repeat immediately. |
| **sequentialByName** | Alphabetical `id` order, wraps. |
| **sequentialByMtime** | `Directory.GetLastWriteTimeUtc` order, cursor `mtimeCursor`. |
| **appMap** (`code.exe` to `["jump_hand"]`) | When foreground exe matches key, only those ids eligible, else all. |

History is atomically written via `ConfigStore.AtomicWrite` (`.tmp` plus `File.Replace`).

---

## 8. Enable / Disable and autostart

| Action | Expected |
|--------|----------|
| **Tray Enable toggle off** | `Pause` plus `ShowWindow(SW_HIDE)` plus `RestoreDesktop` via `IDesktopWallpaper.SetWallpaper` per monitor. Original wallpaper restored from `static/original-wallpaper.tsv`. No SPI loop. |
| **Toggle on** | `Probe` plus `EnsureLayer` plus `Attach` plus `Resume`. Retries applied. |
| **Autostart toggle** | Writes or deletes `HKCU\...\Run\OsageLagtrain` quoted path. `IsEnabled` reflects live registry. No HKLM. |

---

## 9. Uninstall verification

Run `scripts/verify-uninstall.ps1`:

* `reg query HKCU\...\Run /v OsageLagtrain` not found
* `dir %APPDATA%\OsageLagtrain` not found (when Yes)
* `dir %LOCALAPPDATA%\OsageLagtrain` not found (when Yes)
* No `HKLM` or `ProgramData` artifacts

See `UNINSTALL.md`.

---

## Full matrix , fill on run

| # | DPI | Monitors | HDR | Explorer restart | VS Code borderless | QUNS D3D | Result | Notes |
|---|-----|----------|-----|-----------------|--------------------|----------|--------|-------|
| 1 | 100% | 1 | off | no | no | no |  | baseline |
| 2 | 100% | 2 | off | no | no | no |  | per monitor span |
| 3 | 150% | 1 | off | no | no | no |  | bare fix check |
| 4 | 150% | 1 | off | yes | no | no |  | healing 20x300 |
| 5 | 150% | 2 | off | no | yes | no |  | mixed DPI if possible |
| 6 | 150% | 1 | on | no | no | no |  | HDR wash |
| 7 | 150% | 2 | on | no | yes | yes |  | HDR plus borderless plus QUNS |
| 8 | 200% | 1 | off | no | no | no |  | high DPI |
| 9 | 200% | 2 | off | no | no | no |  | high DPI dual |
| 10 | 200% | 1 | off | yes | yes | no |  | restart plus borderless |

For each row log: `topology` (Classic or Raised), `HasIdentityTransform`, `IsPausedByD3D`, `ShQueryCalls`, and whether advance fired once after close.

---

## Automation

```powershell
# Unit and harness tests
dotnet test src\Tests -c Release

# Manual simulation harness uses WindowMonitor.SimulateSequence
# with mock IWindowInterop and injected QUNS state
```

Do NOT use `SystemParametersInfo` in a loop for wallpaper, only `IDesktopWallpaper` per monitor and one SPI fallback on dispose.
