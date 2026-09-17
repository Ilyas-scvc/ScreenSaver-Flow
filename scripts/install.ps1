<#
.SYNOPSIS
    Installs FlowScreen as the current user's screensaver.

.DESCRIPTION
    By default nothing outside your own user profile is touched and no
    administrator rights are needed:

      * files      -> %LOCALAPPDATA%\FlowScreen\app
      * registry   -> HKCU\Control Panel\Desktop

    Exactly three HKCU values are written, all of them the standard, documented
    Windows screensaver settings that the Screen Saver control panel itself uses:

      SCRNSAVE.EXE      full path to FlowScreen.scr
      ScreenSaveActive  "1"  - enables screensavers for this user
      ScreenSaveTimeOut idle seconds before it starts (only with -TimeoutMinutes)

    Nothing machine-wide is modified. scripts/uninstall.ps1 reverses all of it.

    -System additionally copies FlowScreen.scr into %SystemRoot%\System32. That
    needs administrator rights, and its only benefit is that Windows lists the
    screensaver in the Screen Saver Settings dropdown - Windows only enumerates
    .scr files found in System32 and %SystemRoot%. The user-profile install works
    perfectly well without it; the entry simply will not appear in the dropdown
    list (it is still shown as the selected saver).

.PARAMETER Source
    Folder containing the built FlowScreen.scr. Defaults to build/.

.PARAMETER TimeoutMinutes
    Also set the idle timeout. Left alone when not specified.

.PARAMETER System
    Additionally register in System32 so the dropdown lists it. Needs elevation.

.EXAMPLE
    pwsh scripts/install.ps1
.EXAMPLE
    pwsh scripts/install.ps1 -TimeoutMinutes 5
#>
[CmdletBinding()]
param(
    [string]$Source,
    [ValidateRange(1, 600)]
    [int]$TimeoutMinutes = 0,
    [switch]$System
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $Source) { $Source = Join-Path $repoRoot 'build' }

$installDir = Join-Path $env:LOCALAPPDATA 'FlowScreen\app'
$desktopKey = 'HKCU:\Control Panel\Desktop'

function Write-Step([string]$Text) {
    Write-Host ''
    Write-Host "==> $Text" -ForegroundColor Magenta
}

$sourceScr = Join-Path $Source 'FlowScreen.scr'
if (-not (Test-Path $sourceScr)) {
    throw "FlowScreen.scr was not found in '$Source'. Run scripts/build.ps1 first."
}

Write-Host 'FlowScreen install' -ForegroundColor White
Write-Host "  source : $Source"
Write-Host "  target : $installDir"

# ------------------------------------------------------------ copy payload --
Write-Step 'Copying files'
if (Test-Path $installDir) {
    # A running screensaver holds a lock on its own file.
    Get-Process -Name 'FlowScreen' -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "    stopping a running FlowScreen (pid $($_.Id))"
        $_.Kill()
        $_.WaitForExit(5000) | Out-Null
    }
    Remove-Item $installDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item -Path (Join-Path $Source '*') -Destination $installDir -Recurse -Force

$installedScr = Join-Path $installDir 'FlowScreen.scr'
Write-Host "    installed $installedScr"

# --------------------------------------------------------------- registry ---
Write-Step 'Registering with Windows (HKCU only)'
New-ItemProperty -Path $desktopKey -Name 'SCRNSAVE.EXE' -Value $installedScr -PropertyType String -Force | Out-Null
Write-Host "    SCRNSAVE.EXE     = $installedScr"

New-ItemProperty -Path $desktopKey -Name 'ScreenSaveActive' -Value '1' -PropertyType String -Force | Out-Null
Write-Host '    ScreenSaveActive = 1'

if ($TimeoutMinutes -gt 0) {
    $seconds = $TimeoutMinutes * 60
    New-ItemProperty -Path $desktopKey -Name 'ScreenSaveTimeOut' -Value "$seconds" -PropertyType String -Force | Out-Null
    Write-Host "    ScreenSaveTimeOut = $seconds seconds ($TimeoutMinutes min)"
}
else {
    $current = (Get-ItemProperty -Path $desktopKey -Name 'ScreenSaveTimeOut' -ErrorAction SilentlyContinue).'ScreenSaveTimeOut'
    if ($current) { Write-Host "    ScreenSaveTimeOut left at $current seconds" }
}

# ----------------------------------------------------------- System32 copy --
if ($System) {
    Write-Step 'Registering in System32 (requires administrator)'
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Warning 'Not running elevated: skipping the System32 copy. Re-run this script from an administrator shell to have FlowScreen listed in the Screen Saver Settings dropdown.'
    }
    else {
        $system32 = Join-Path $env:SystemRoot 'System32\FlowScreen.scr'
        Copy-Item -Path $installedScr -Destination $system32 -Force
        New-ItemProperty -Path $desktopKey -Name 'SCRNSAVE.EXE' -Value $system32 -PropertyType String -Force | Out-Null
        Write-Host "    copied to $system32"
        Write-Host "    SCRNSAVE.EXE now points at the System32 copy"
    }
}

Write-Host ''
Write-Host 'FlowScreen is installed.' -ForegroundColor Green
Write-Host ''
Write-Host 'Try it:' -ForegroundColor White
Write-Host "  Settings   : `"$installedScr`" /c"
Write-Host "  Full screen: `"$installedScr`" /s"
Write-Host '  Windows UI : Settings > Personalization > Lock screen > Screen saver'
Write-Host ''
Write-Host "Remove with: pwsh scripts/uninstall.ps1"
