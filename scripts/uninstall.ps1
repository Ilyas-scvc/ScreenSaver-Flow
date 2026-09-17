<#
.SYNOPSIS
    Removes FlowScreen.

.DESCRIPTION
    Undoes everything scripts/install.ps1 did:

      * clears HKCU\Control Panel\Desktop\SCRNSAVE.EXE, but only when it still
        points at a FlowScreen executable - another screensaver selected since
        the install is left alone
      * deletes %LOCALAPPDATA%\FlowScreen\app
      * deletes %SystemRoot%\System32\FlowScreen.scr when it exists and the shell
        is elevated

    Settings and logs under %LOCALAPPDATA%\FlowScreen are kept unless -Purge is
    given, so reinstalling restores your configuration.

.PARAMETER Purge
    Also delete settings.json, the logs and the WebView2 profile.

.EXAMPLE
    pwsh scripts/uninstall.ps1
.EXAMPLE
    pwsh scripts/uninstall.ps1 -Purge
#>
[CmdletBinding()]
param(
    [switch]$Purge
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Join-Path $env:LOCALAPPDATA 'FlowScreen'
$installDir = Join-Path $root 'app'
$desktopKey = 'HKCU:\Control Panel\Desktop'

function Write-Step([string]$Text) {
    Write-Host ''
    Write-Host "==> $Text" -ForegroundColor Magenta
}

Write-Host 'FlowScreen uninstall' -ForegroundColor White

# --------------------------------------------------------------- processes --
Get-Process -Name 'FlowScreen' -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "    stopping FlowScreen (pid $($_.Id))"
    $_.Kill()
    $_.WaitForExit(5000) | Out-Null
}

# ---------------------------------------------------------------- registry --
Write-Step 'Clearing the screensaver registration'
$current = (Get-ItemProperty -Path $desktopKey -Name 'SCRNSAVE.EXE' -ErrorAction SilentlyContinue).'SCRNSAVE.EXE'

if (-not $current) {
    Write-Host '    SCRNSAVE.EXE was not set'
}
elseif ($current -like '*FlowScreen*') {
    Remove-ItemProperty -Path $desktopKey -Name 'SCRNSAVE.EXE' -ErrorAction SilentlyContinue
    Write-Host "    removed SCRNSAVE.EXE (was $current)"
    Write-Host '    ScreenSaveActive left as-is so other screensavers keep working'
}
else {
    Write-Host "    SCRNSAVE.EXE points at another screensaver, leaving it alone:"
    Write-Host "      $current"
}

# ------------------------------------------------------------------- files --
Write-Step 'Removing files'
if (Test-Path $installDir) {
    Remove-Item $installDir -Recurse -Force
    Write-Host "    deleted $installDir"
}
else {
    Write-Host "    $installDir was not present"
}

$system32 = Join-Path $env:SystemRoot 'System32\FlowScreen.scr'
if (Test-Path $system32) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Remove-Item $system32 -Force
        Write-Host "    deleted $system32"
    }
    else {
        Write-Warning "$system32 still exists. Re-run this script elevated to remove it."
    }
}

# ------------------------------------------------------------------- purge --
if ($Purge) {
    Write-Step 'Purging settings and logs'
    if (Test-Path $root) {
        Remove-Item $root -Recurse -Force
        Write-Host "    deleted $root"
    }
}
elseif (Test-Path $root) {
    Write-Host ''
    Write-Host "Settings and logs kept in $root (re-run with -Purge to delete them)."
}

Write-Host ''
Write-Host 'FlowScreen has been removed.' -ForegroundColor Green
