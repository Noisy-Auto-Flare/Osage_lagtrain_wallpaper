# Osage Lag Train Wallpaper

![Windows 11 only](https://img.shields.io/badge/Windows-11%20only-0078D4) ![PerMonitorV2](https://img.shields.io/badge/DPI-PerMonitorV2-blue) ![License](https://img.shields.io/badge/license-personal%20use-lightgrey)

B and W cycles wallpaper for Windows 11. Each folder under `cycles` is a scene. Frames play when a maximized or fullscreen window closes and the desktop becomes foreground again. No SPI loop, no bundled frames.

> **Source:** Inabakumori , Lagtrain (ラグトレイン) feat. Kaai Yuki , https://www.youtube.com/watch?v=UnIhRpIT7nc , frames are NOT bundled, extract locally for personal use only. See [Legal](#legal--credits) and `docs/ffmpeg-recipe.md`.

---

## Requirements

* Windows 11 22H2 or newer (24H2 tested). Windows 10 is not supported.
* x64. No MSIX, no admin, no service.
* .NET 8 runtime is bundled in the published single file, no separate install needed for the portable zip.

---

## Quick start

### Portable (recommended)

Portability is decided by a **writability probe**, not by path name. On launch the app tries `File.Create(exeDir/.writetest)`:

* if it succeeds, portable storage is used: `.\cycles\`, `.\settings.json`, `.\history.json` next to `OsageLagtrain.exe`
* if it throws `UnauthorizedAccessException` or `IOException` (for example `C:\Program Files\`), fallback is `%APPDATA%\OsageLagtrain\` and `%LOCALAPPDATA%\OsageLagtrain\static\`

```powershell
Expand-Archive OsageLagtrain.zip -DestinationPath D:\tmp\Osage
D:\tmp\Osage\OsageLagtrain.exe
# or verify without launching wallpaper:
D:\tmp\Osage\OsageLagtrain.exe --verify-cycles
```

### Installed (Inno Setup)

Run `OsageLagtrain-Setup.exe` (per user, `PrivilegesRequired=lowest`, HKCU only). Default install dir is `%LOCALAPPDATA%\OsageLagtrain`.

* `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value `OsageLagtrain` is created with `uninsdeletevalue`, so uninstall removes it.
* User data lives in `%APPDATA%\OsageLagtrain` (settings, history, cycles fallback) and `%LOCALAPPDATA%\OsageLagtrain\static` (original wallpaper snapshot).
* On uninstall you are asked `Delete user data?` , Yes deletes both dirs via `DelTree`, No keeps them.

---

## How to launch

```powershell
# Portable
.\OsageLagtrain.exe

# Verification (no wallpaper, just checks cycles)
.\OsageLagtrain.exe --verify-cycles
# -> template OK, N real scenes
# -> template missing (if cycles\_template\scene.json not found)

# Installed (after setup)
"%LOCALAPPDATA%\OsageLagtrain\OsageLagtrain.exe"
```

Tray icon appears. Right click for menu. Settings window lists scenes with live validation badges.

If `cycles` is missing next to the exe and no fallback exists, the app creates the fallback dir on first save and logs `template missing`.

---

## Enable / Disable (tray toggle)

Tray menu has **Enable** with a live checkmark (`MF_CHECKED` reflects `EnableManager.IsEnabled`):

* **Enable checked (default):** `Probe()` + `EnsureLayer()` + `Attach(hwnd)` + `Monitor.Resume()`. Probe retries 20 times with 300 ms between tries if Explorer just restarted.
* **Enable unchecked:** `Monitor.Pause()` + `ShowWindow(SW_HIDE)` + `RestoreDesktop()` via `IDesktopWallpaper.SetWallpaper` per monitor from `%LOCALAPPDATA%\OsageLagtrain\static\original-wallpaper.tsv`. This restores your real wallpaper. SPI is NOT called here. SPI fallback only runs on final `Dispose` (app exit).

Toggle is immediate, you can re enable without restarting. When disabled the wallpaper window is hidden and window monitoring is paused.

Session helpers: lock, display off, suspend all call `PauseForSession`, unlock, display on, resume call `ResumeFromSession`.

---

## Autostart (HKCU Run)

Tray menu has **Autostart** with a live checkmark reading `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\OsageLagtrain`:

* Value data is a quoted exe path: `"D:\tmp\Osage\OsageLagtrain.exe"` or `"%LOCALAPPDATA%\OsageLagtrain\OsageLagtrain.exe"`.
* Toggle writes or deletes that value via `AutostartManager`. No HKLM, no Task Scheduler, no service.
* `IsEnabled` checks `HKCU\...\Run` each time you open the menu, so external edits are reflected.

Manual control:

```powershell
# Check
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v OsageLagtrain

# Remove manually
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v OsageLagtrain /f

# Add manually (portable path example)
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v OsageLagtrain /t REG_SZ /d "\"D:\tmp\Osage\OsageLagtrain.exe\"" /f
```

SingleFile publish note: autostart uses `Environment.ProcessPath` first, then `Assembly.Location`, then `AppContext.BaseDirectory` fallback, so the quoted path stays correct for single file.

---

## How to create a scene

Each subfolder of `cycles` is a scene. Frames are natural sorted via `StrCmpLogicalW`, so `0002.png` comes before `0010.png`.

### 1. Copy the template

```powershell
Copy-Item -Recurse cycles\_template cycles\my_scene
dir cycles\my_scene
# scene.json  0001.png (1x1 #b2b2b2 placeholder)
```

### 2. Edit scene.json

Schema: `docs/scene.json.schema.json` (draft 2020-12). Only `id` is required.

```json
{"id":"jump_hand","fps":12,"mode":"once","holdLastMs":800}
```

| Field | Type | Limits | Default |
|-------|------|--------|---------|
| `id` | string | `^[a-z0-9_-]{1,32}$` | required |
| `title` | string | 1 to 128 | `id` |
| `fps` | integer | 1 to 30 | `12` |
| `mode` | string or object | `"once"`, `"loop"`, `"pingpong"`, `{"count":1..100}` | `"once"` |
| `holdLastMs` | integer | 0 to 5000 | `0` |
| `postEventDelayMs` | integer | 0 to 5000 | global `500` |
| `idleColor` | string | `^#[0-9a-fA-F]{6}$` | `"#b2b2b2"` |

More examples:

```json
{"id":"loop_run","title":"Loop Run","fps":12,"mode":"loop"}
{"id":"ping_pong","fps":8,"mode":"pingpong","holdLastMs":200}
{"id":"three_times","fps":15,"mode":{"count":3},"holdLastMs":500,"postEventDelayMs":1200}
```

Invalid files are not ignored silently. The loader throws `SchemaValidationException` with `path#line` detail and the UI shows a red badge with that message.

Full guide: `docs/scenes/README.md`. Settings and history schemas: `docs/settings.schema.json`, `docs/history.schema.json`.

### 3. Add frames

Replace `0001.png` with your own frames. Supported: `.png`, `.jpg`, `.jpeg`, `.webp` (WebP skipped with a toast if WIC codec is missing, OneDrive Offline files skipped).

```powershell
# After extracting via docs/ffmpeg-recipe.md
Copy-Item final\*.png cycles\my_scene\
# Result: cycles\my_scene\0001.png, 0002.png, ... 0120.png
```

Name with leading zeros `0001` to `9999`. Do not put fps in the file name, fps lives only in `scene.json`. Sort is natural, not lexical.

### 4. Select and play

Restart the app or pick the scene in Settings. Playback triggers when a covering window closes and the desktop becomes foreground. Selection policy is `randomNoRepeat` with window 3 by default, see `selectionPolicy` and `noRepeatWindow` in settings.

See `docs/ffmpeg-recipe.md` for the full `yt-dlp` + `ffmpeg` pipeline (`fps=1,mpdecimate`, `crop`, `eq=saturation=0`) and `cycles/README.md` for portable vs installed details.

---

## Rendering notes

* **PerMonitorV2** is set on the app manifest. DPI is handled per monitor via `MapWindowPoints` and `GetDpiForWindow`, never raw `0,0` on raised desktop.
* **Raised Classic distinction:** `Probe()` checks `WS_EX_NOREDIRECTIONBITMAP` on Progman. Raised uses `SetParent(Progman)` and slots under `SHELLDLL_DefView` with `SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE`. Classic uses `WorkerW` and `HWND_BOTTOM`. Raised never uses `HWND_BOTTOM`.
* **HiDPI bare fix:** raised path uses DirectComposition `CreateTargetForHwnd(hwnd, true)` with identity `1:1` physical transform. PerMonitorV2 alone without identity leaves 55 percent bare at 150 percent scale. CompositionHost falls back to WriteableBitmap if DComp is unavailable.
* **Healing:** `TaskbarCreated` (`RegisterWindowMessage`), `EVENT_OBJECT_DESTROY` (`SetWinEventHook`), `WM_DISPLAYCHANGE`, `WM_DPICHANGED`, `WTS_SESSION_UNLOCK` all re probe with 20 times 300 ms retry.

---

## Window monitoring and QUNS nuance

Wallpaper advances only after a **covering** window closes and the desktop is foreground. Covering means:

* `IsZoomed` fast path, or
* `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` vs `rcMonitor` or `rcWork` with 95 percent coverage (each dimension 95 percent or area 95 percent)

Filters: visible, not iconic, not cloaked (`DWMWA_CLOAKED`), not tool window (`WS_EX_TOOLWINDOW`), not foreign ancestor.

Debounce 500 ms, WinEvent hooks for `FOREGROUND`, `MINIMIZESTART/END`, `MOVESIZESTART/END`, `OBJECT_DESTROY`, plus a 500 ms fallback poll. `LOCATIONCHANGE (0x800B)` is intentionally NOT subscribed.

**QUNS D3D fullscreen pause:** `SHQueryUserNotificationState` is queried and cached for 500 ms. If state is `QUNS_RUNNING_D3D_FULL_SCREEN` (value 3, compat value 7 also handled), monitoring pauses (`pausedByD3D`) and no advance fires. This covers exclusive fullscreen games and borderless D3D fullscreen. When the state returns to `QUNS_ACCEPTS_NOTIFICATIONS`, or on next `TriggerEvaluate`, pause clears and covering tracking resumes. Poll caching avoids spamming `SHQueryUserNotificationState` every event.

Per scene `postEventDelayMs` overrides global 500 ms. When `Interval=0` (DirectComposition tick) the scheduler skips `CompositionTarget.Rendering` during pause.

---

## Troubleshooting

| Symptom | Cause / Fix |
|---------|-------------|
| **24H2 raised desktop, wallpaper not visible or z order wrong** | 24H2 uses raised `WS_EX_NOREDIRECTIONBITMAP` Progman. App must probe each time via `FindWindow("Progman")` and `GetWindowLongPtr(GWL_EXSTYLE)`. Do not cache Progman HWND. Raised path parents to Progman and inserts under `SHELLDLL_DefView`, never `HWND_BOTTOM`. If you see wallpaper under icons, you are on raised and used the classic path. Restart app, check logs for `RaisedDesktop=true`. Healing retries 20 times 300 ms after Explorer restart. |
| **HiDPI 150 percent, 55 percent of screen bare** | PerMonitorV2 without DComp identity leaves logical pixels mapped. Fix is `CompositionHost.TryCreateTargetForHwnd(hwnd, true)` with identity `1:1` transform. `HasIdentityTransform` must be true. Fallback WriteableBitmap also applies `MapWindowPoints` plus `DpiScale`, but DComp is the primary fix. Verify `GetDpiForWindow` scale and that `TrySetWallpaperSpan` uses scaled width and height, not raw `0,0`. |
| **Multi monitor, wallpaper spans wrong or shows on one screen** | Check `DisplayManager.EnumerateMonitors` and `VirtualScreenBounds`. Per monitor `SetWindowPos` per `rcMonitor` is used for per screen, span uses virtual screen bounds mapped via `MapWindowPoints(0, Progman)`. Ensure `MapWindowPoints` is called, not literal `0,0`. Test `1` vs `2` monitors at `100`, `150`, `200` percent in matrix. |
| **VS Code borderless maximized triggers or does not trigger** | VS Code borderless uses `DWMWA_EXTENDED_FRAME_BOUNDS` that equals `rcWork` or `rcMonitor` at 95 percent. `CoversMonitor` checks both `rcMonitor` and `rcWork` at 95 percent threshold. If borderless has a thin border, it still passes `IsZoomed` or area 95 percent. If it should not trigger, adjust coverage threshold or add exe to `appMap`. Alt Tab to a small window correctly clears `previousWasCovering`, so next desktop does not fire spuriously. |
| **Explorer restart, wallpaper disappears** | Expected briefly. Healing listens to `TaskbarCreated`, `EVENT_OBJECT_DESTROY`, `WM_DISPLAYCHANGE`, `WM_DPICHANGED`. It re probes and re attaches. If it stays gone, toggle Enable off and on, or restart app. Do not kill `explorer.exe` repeatedly, wait 1 sec between restarts for retry loop. |
| **Fullscreen game pauses wallpaper, but pause sticks after exit** | QUNS cache is 500 ms. After leaving D3D fullscreen, `QUNS_ACCEPTS_NOTIFICATIONS` resumes. If stuck, move focus to desktop and back, or wait 600 ms. Check `IsPausedByD3D` and `ShQueryCalls` in logs. Compat value `7` is also treated as D3D. |
| **HDR wash / colors look faded** | DComp mitigates HDR wash in raised path. v1 has no HDR color management. If wash persists on HDR monitor, try disabling HDR for desktop or use `idleColor` to match. |
| **WebP frames skipped** | Toast `Skipping webp ... WIC WebP codec not available`. Install WebP WIC codec or convert to PNG via `ffmpeg -i in.webp out.png`. OneDrive Offline files are also skipped. |
| **History grows forever** | Capped at 1024 bytes atomically via `ConfigStore.AtomicWrite` (temp file plus `File.Replace`). `recent` is truncated to `noRepeatWindow` (default 3) and further if over 1 KB. `0` means no dedup window. |

Do not use `SystemParametersInfo` in a loop to set wallpaper. The app uses `IDesktopWallpaper.SetWallpaper` per monitor and only falls back to `SPI_SETDESKWALLPAPER` once on final dispose.

---

## Configuration

Settings live in `settings.json` next to the exe (portable) or `%APPDATA%\OsageLagtrain\settings.json` (installed). Schema: `docs/settings.schema.json`.

```json
{
  "cyclesRoot": "./cycles",
  "postEventDelayMs": 500,
  "selectionPolicy": "randomNoRepeat",
  "noRepeatWindow": 3,
  "idleColor": "#b2b2b2",
  "autostart": false,
  "appMap": {
    "code.exe": ["jump_hand", "loop_run"]
  }
}
```

* `selectionPolicy`: `randomNoRepeat` (default, window N=3 avoids last 3), `randomPure`, `sequentialByName`, `sequentialByMtime`
* `noRepeatWindow`: `0` to `20`, `0` disables dedup
* `appMap`: per exe scene whitelist, fallback to all scenes if empty

History is `history.json` (`recent` plus `mtimeCursor`), atomically written and truncated.

---

## Legal / Credits

* **Source:** Inabakumori (稲葉曇) , Lagtrain (ラグトレイン) feat. Kaai Yuki , https://www.youtube.com/watch?v=UnIhRpIT7nc
* **This repo does NOT bundle Lagtrain frames.** `cycles/_template/0001.png` is a 1x1 `#b2b2b2` placeholder. Real frames must be extracted locally by the user for personal wallpaper use only. See `docs/ffmpeg-recipe.md` for `yt-dlp` plus `ffmpeg` commands.
* **Japan Copyright Act Article 30:** private use copy allowed, redistribution prohibited.
* **US Fair Use (17 USC 107):** personal non commercial use may be fair use, not for redistribution.
* **YouTube ToS:** download only via official means, `yt-dlp` is at your own risk for personal archive.
* Do NOT commit real frames to git, do NOT redistribute the frames archive. If the rightsholder requests removal, delete local frames.
* This tool is not affiliated with Inabakumori or Kaai Yuki.

See `CREDITS.txt` and `docs/ffmpeg-recipe.md`.

---

## Uninstall

See `UNINSTALL.md` for manual sweep commands and `scripts/verify-uninstall.ps1` for automated check.

Quick check:

```powershell
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v OsageLagtrain
dir "%APPDATA%\OsageLagtrain"
dir "%LOCALAPPDATA%\OsageLagtrain"
powershell -ExecutionPolicy Bypass -File scripts\verify-uninstall.ps1
```

---

## Docs map

* `cycles/README.md` , portable vs installed, how to create a scene
* `docs/scenes/README.md` , scene.json spec and examples
* `docs/scene.json.schema.json` , JSON schema for scene
* `docs/settings.schema.json` , global settings schema
* `docs/history.schema.json` , history schema
* `docs/ffmpeg-recipe.md` , `yt-dlp` plus `ffmpeg` extraction recipe
* `docs/QA-matrix.md` , QA matrix across DPI, monitors, HDR, Explorer restart
* `CREDITS.txt` , source and legal
* `UNINSTALL.md` , clean uninstall
* `installer.iss` , Inno Setup script (HKCU only, per user)

---

## Build

```powershell
dotnet build OsageLagtrain.sln -c Release
dotnet test src\Tests -c Release
dotnet publish src\App -c Release -r win-x64 --self-contained
```

No frames are bundled in the build output. Verify with `git status` that no real frames are tracked.
