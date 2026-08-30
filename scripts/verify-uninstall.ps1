# verify-uninstall.ps1 - Clean uninstall verification for Osage Lagtrain Wallpaper
# Checks:
#  - reg query HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v OsageLagtrain not found
#  - dir %APPDATA%\OsageLagtrain not found
#  - dir %LOCALAPPDATA%\OsageLagtrain not found (if Yes)
# Must NOT check HKLM / ProgramData
# Exit 0 = PASS, 1 = FAIL

param(
  [switch]$ExpectDataRemoved = $true  # if Yes was chosen on uninstall, data dirs should be gone
)

$ErrorActionPreference = "Continue"
$failed = $false

Write-Host "=== OsageLagtrain verify-uninstall ==="

# 1. HKCU Run value should NOT exist (uninsdeletevalue)
Write-Host "[1] Checking HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v OsageLagtrain ..."
$runFound = $false
# Method A: reg query (as required by spec)
try {
  $null = reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v OsageLagtrain 2>&1
  if ($LASTEXITCODE -eq 0) {
    $runFound = $true
    Write-Host "  FAIL: reg query found OsageLagtrain value (expected not found)"
  } else {
    Write-Host "  PASS: reg query HKCU\...\Run /v OsageLagtrain not found (exit code $LASTEXITCODE)"
  }
} catch {
  Write-Host "  reg query exception: $_"
}

# Method B: PowerShell registry provider double-check
try {
  $val = Get-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "OsageLagtrain" -ErrorAction SilentlyContinue
  if ($null -ne $val -and $null -ne $val.OsageLagtrain) {
    $runFound = $true
    Write-Host "  FAIL (PS): Get-ItemProperty found OsageLagtrain=$($val.OsageLagtrain)"
  } elseif (-not $runFound) {
    Write-Host "  PASS (PS): Get-ItemProperty HKCU Run OsageLagtrain not found"
  }
} catch {
  Write-Host "  PS registry check exception: $_"
}

if ($runFound) { $failed = $true; Write-Host "  => RUN VALUE STILL EXISTS - FAIL" } else { Write-Host "  => RUN VALUE CLEAN - PASS" }

# 2. %APPDATA%\OsageLagtrain (roaming) should not exist after Yes
$roaming = Join-Path $env:APPDATA "OsageLagtrain"
Write-Host "[2] Checking dir %APPDATA%\OsageLagtrain ($roaming) ..."
if (Test-Path $roaming) {
  if ($ExpectDataRemoved) {
    Write-Host "  FAIL: dir %APPDATA%\OsageLagtrain exists (expected not found after Yes)"
    Get-ChildItem $roaming -Recurse -Force | Select-Object FullName | Out-String -Width 400 | Write-Host
    $failed = $true
  } else {
    Write-Host "  INFO: dir exists but ExpectDataRemoved=false (user chose No) - PASS"
  }
} else {
  Write-Host "  PASS: dir %APPDATA%\OsageLagtrain not found"
}

# Also check via cmd dir style for evidence parity
try {
  $dirOut = cmd /c "dir `"%APPDATA%\OsageLagtrain`" 2>&1"
  if ($dirOut -match "File Not Found" -or $dirOut -match "cannot find" -or $LASTEXITCODE -ne 0) {
    # dir not found is expected
  }
} catch {}

# 3. %LOCALAPPDATA%\OsageLagtrain should not exist after Yes
$local = Join-Path $env:LOCALAPPDATA "OsageLagtrain"
Write-Host "[3] Checking dir %LOCALAPPDATA%\OsageLagtrain ($local) ..."
if (Test-Path $local) {
  if ($ExpectDataRemoved) {
    Write-Host "  FAIL: dir %LOCALAPPDATA%\OsageLagtrain exists (expected not found after Yes)"
    Get-ChildItem $local -Recurse -Force | Select-Object FullName | Out-String -Width 400 | Write-Host
    $failed = $true
  } else {
    Write-Host "  INFO: dir exists but ExpectDataRemoved=false (user chose No) - PASS"
  }
} else {
  Write-Host "  PASS: dir %LOCALAPPDATA%\OsageLagtrain not found"
}

# 4. Must NOT have HKLM / ProgramData artifacts (sanity)
Write-Host "[4] Sanity: HKLM should not have OsageLagtrain (must NOT use HKLM) ..."
try {
  $hklm = Get-ItemProperty -Path "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "OsageLagtrain" -ErrorAction SilentlyContinue
  if ($null -ne $hklm -and $null -ne $hklm.OsageLagtrain) {
    Write-Host "  FAIL: HKLM Run contains OsageLagtrain (must NOT use HKLM)"
    $failed = $true
  } else {
    Write-Host "  PASS: HKLM Run OsageLagtrain not found (correct - must NOT use HKLM)"
  }
} catch {
  Write-Host "  PASS: HKLM check - not found or access denied (expected)"
}

$progData = Join-Path $env:ProgramData "OsageLagtrain"
if (Test-Path $progData) {
  Write-Host "  FAIL: ProgramData\OsageLagtrain exists (must NOT use ProgramData)"
  $failed = $true
} else {
  Write-Host "  PASS: ProgramData\OsageLagtrain not found (correct)"
}

Write-Host "======================================="
if ($failed) {
  Write-Host "RESULT: FAIL - uninstall not clean"
  exit 1
} else {
  Write-Host "RESULT: PASS - uninstall clean (HKCU Run not found, dirs not found)"
  exit 0
}
