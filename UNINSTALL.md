# Uninstall , Osage Lagtrain Wallpaper

Clean uninstall must leave no HKCU Run value and no user data dirs, and must restore the original desktop wallpaper.

---

## Method 1: Via installer (recommended)

If installed via `OsageLagtrain-Setup.exe`:

1. Settings then Apps then Installed apps then Osage Lagtrain Wallpaper then Uninstall
2. Or run `unins000.exe` in `%LOCALAPPDATA%\OsageLagtrain`
3. When asked `Delete user data (settings/history/cycles)?` choose **Yes** to remove `%APPDATA%\OsageLagtrain` and `%LOCALAPPDATA%\OsageLagtrain`, or **No** to keep them

The installer handles `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\OsageLagtrain` automatically via `uninsdeletevalue`.

After uninstall, run verification:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\verify-uninstall.ps1
# or with explicit expectation:
powershell -ExecutionPolicy Bypass -File scripts\verify-uninstall.ps1 -ExpectDataRemoved:$true
```

---

## Method 2: Portable , manual sweep

If you ran the portable zip, close the app first (tray then Exit). Exit triggers `RestoreDesktop()` via `IDesktopWallpaper.SetWallpaper` per monitor from `%LOCALAPPDATA%\OsageLagtrain\static\original-wallpaper.tsv`, plus final `SPI_SETDESKWALLPAPER` fallback on dispose. Then run:

### 1. Check and remove autostart value

```powershell
# Check (should be not found after clean uninstall)
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v OsageLagtrain

# If found, delete
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v OsageLagtrain /f

# Verify gone
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v OsageLagtrain
# Expected: ERROR: The system was unable to find the specified registry key or value.
```

PowerShell alternative:

```powershell
Get-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name OsageLagtrain -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name OsageLagtrain -ErrorAction SilentlyContinue
```

Must NOT touch `HKLM\Software\Microsoft\Windows\CurrentVersion\Run` , the app never uses it.

### 2. Remove user data dirs

```powershell
# Check
dir "%APPDATA%\OsageLagtrain"
dir "%LOCALAPPDATA%\OsageLagtrain"

# Remove (both roaming and local)
rmdir /s /q "%APPDATA%\OsageLagtrain"
rmdir /s /q "%LOCALAPPDATA%\OsageLagtrain"

# PowerShell variant
Remove-Item -Recurse -Force "$env:APPDATA\OsageLagtrain" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\OsageLagtrain" -ErrorAction SilentlyContinue

# Verify gone
dir "%APPDATA%\OsageLagtrain"
# Expected: File Not Found
dir "%LOCALAPPDATA%\OsageLagtrain"
# Expected: File Not Found
```

Contents removed:

* `%APPDATA%\OsageLagtrain\settings.json`, `history.json`, `appMap.json`, `cycles\` (fallback when portable not writable)
* `%LOCALAPPDATA%\OsageLagtrain\static\original-wallpaper.txt` and `.tsv` (original wallpaper snapshot)
* `%LOCALAPPDATA%\OsageLagtrain\` when installed via Inno

Must NOT delete `C:\ProgramData\OsageLagtrain` , the app never uses ProgramData.

### 3. Restore original wallpaper

Normally handled on Exit or Disable. If you killed the process and wallpaper is stuck:

* The snapshot is at `%LOCALAPPDATA%\OsageLagtrain\static\original-wallpaper.tsv` (TSV `monitorId<TAB>path` plus `original-wallpaper.txt` compat)
* On next launch, Enable off then on triggers `RestoreDesktop()` via `IDesktopWallpaper.SetWallpaper` per monitor
* On final app Dispose, fallback `SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, NULL, SPIF_UPDATEINIFILE|SPIF_SENDCHANGE)` resets the wallpaper
* Manual fallback if snapshot is already deleted:

```powershell
# Reset to solid color or Windows default via SPI (only as last resort, not in a loop)
# This matches the Dispose fallback, do not call in a loop
rundll32.exe user32.dll,UpdatePerUserSystemParameters
# Or pick a wallpaper via Settings then Personalization then Background
```

### 4. Remove portable folder itself

```powershell
# If you extracted to D:\tmp\Osage
rmdir /s /q "D:\tmp\Osage"
```

---

## Verification , automated

```powershell
# From repo root
powershell -ExecutionPolicy Bypass -File scripts\verify-uninstall.ps1

# If you kept data (chose No on uninstall prompt)
powershell -ExecutionPolicy Bypass -File scripts\verify-uninstall.ps1 -ExpectDataRemoved:$false
```

Checks performed:

1. `reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v OsageLagtrain` , must be not found (exit code 1)
2. `dir "%APPDATA%\OsageLagtrain"` , must be not found when `ExpectDataRemoved=true`
3. `dir "%LOCALAPPDATA%\OsageLagtrain"` , must be not found when `ExpectDataRemoved=true`
4. Sanity: `HKLM\...\Run` and `ProgramData\OsageLagtrain` must NOT exist (app never uses them)

Exit code `0` is PASS, `1` is FAIL.

---

## What must NOT happen

* Do NOT bundle `cycles` frames in the uninstall , they were never installed, they are user local
* Do NOT loop `SystemParametersInfo` , the app calls `IDesktopWallpaper.SetWallpaper` per monitor on Disable and only one SPI fallback on Dispose
* Do NOT leave `HKCU Run` value , Inno uses `uninsdeletevalue`, portable must `reg delete`
* Do NOT use `HKLM` or `ProgramData` , per user only, `PrivilegesRequired=lowest`

---

## Troubleshooting uninstall

| Problem | Fix |
|---------|-----|
| `reg query` still finds value | Run `reg delete ... /f` as the same user (not admin). Check `HKCU`, not `HKLM`. Restart Explorer or log off if cached. |
| `dir` still shows folder | Close app first, then `rmdir`. If access denied, check if `OsageLagtrain.exe` still running in Task Manager. |
| Wallpaper not restored | Re launch app, toggle Enable off then on, then Exit. Check `%LOCALAPPDATA%\OsageLagtrain\static\original-wallpaper.tsv` exists before deleting. |
| Inno uninstall did not ask about data | Older build without `CurUninstallStepChanged` DelTree prompt , use manual sweep above. |
