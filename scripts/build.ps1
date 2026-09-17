<#
.SYNOPSIS
    Builds FlowScreen end to end and produces FlowScreen.scr.

.DESCRIPTION
    1. installs the renderer's npm dependencies (npm ci when a lockfile exists)
    2. type-checks and bundles the renderer with Vite
    3. copies dist/ into the Windows project so MSBuild embeds it as resources
    4. publishes the WPF host
    5. copies the executable to FlowScreen.scr

    The .scr is a byte-for-byte copy of the .exe. Windows identifies a
    screensaver purely by the file extension, so nothing else is required.

.PARAMETER Configuration
    Release (default) or Debug. Debug builds load the renderer from the Vite dev
    server at http://127.0.0.1:5173 instead of the embedded bundle.

.PARAMETER SkipRenderer
    Reuse whatever is already in src/windows/FlowScreen/Assets/renderer.

.PARAMETER SingleFile
    Publish as one self-extracting executable (default). Turn it off to get a
    plain folder publish, which is easier to debug.

.PARAMETER Output
    Where the finished artifacts are written. Defaults to build/.

.EXAMPLE
    pwsh scripts/build.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [switch]$SkipRenderer,

    [bool]$SingleFile = $true,

    [string]$Output
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$rendererDir = Join-Path $repoRoot 'src/renderer'
$hostDir = Join-Path $repoRoot 'src/windows/FlowScreen'
$assetsDir = Join-Path $hostDir 'Assets/renderer'
$publishDir = Join-Path $repoRoot 'artifacts/publish'
if (-not $Output) { $Output = Join-Path $repoRoot 'build' }

function Write-Step([string]$Text) {
    Write-Host ''
    Write-Host "==> $Text" -ForegroundColor Magenta
}

function Assert-Tool([string]$Name, [string]$Hint) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name was not found on PATH. $Hint"
    }
}

Write-Host 'FlowScreen build' -ForegroundColor White
Write-Host "  repository    : $repoRoot"
Write-Host "  configuration : $Configuration"
Write-Host "  single file   : $SingleFile"
Write-Host "  output        : $Output"

# ---------------------------------------------------------------- renderer --
if (-not $SkipRenderer) {
    Assert-Tool 'npm' 'Install Node.js 20.19+ from https://nodejs.org/'

    Write-Step 'Installing renderer dependencies'
    Push-Location $rendererDir
    try {
        if (Test-Path (Join-Path $rendererDir 'package-lock.json')) {
            & npm ci --no-audit --no-fund
        }
        else {
            & npm install --no-audit --no-fund
        }
        if ($LASTEXITCODE -ne 0) { throw "npm install failed with exit code $LASTEXITCODE" }

        Write-Step 'Building the renderer (tsc + vite)'
        & npm run build
        if ($LASTEXITCODE -ne 0) { throw "npm run build failed with exit code $LASTEXITCODE" }
    }
    finally {
        Pop-Location
    }

    Write-Step 'Copying the renderer bundle into the Windows project'
    $dist = Join-Path $rendererDir 'dist'
    if (-not (Test-Path (Join-Path $dist 'index.html'))) {
        throw "The renderer build produced no index.html in $dist"
    }

    if (Test-Path $assetsDir) {
        Get-ChildItem $assetsDir -Force |
            Where-Object { $_.Name -ne '.gitkeep' } |
            Remove-Item -Recurse -Force
    }
    else {
        New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null
    }

    Copy-Item -Path (Join-Path $dist '*') -Destination $assetsDir -Recurse -Force

    $embedded = Get-ChildItem $assetsDir -Recurse -File | Where-Object { $_.Name -ne '.gitkeep' }
    $totalKb = [math]::Round((($embedded | Measure-Object Length -Sum).Sum) / 1KB, 1)
    Write-Host "    $($embedded.Count) file(s), $totalKb KB:"
    $embedded | ForEach-Object { Write-Host "      $($_.Name)  ($([math]::Round($_.Length / 1KB, 1)) KB)" }
}
else {
    Write-Step 'Skipping the renderer build (-SkipRenderer)'
}

# -------------------------------------------------------------------- host --
Assert-Tool 'dotnet' 'Install the .NET 8 SDK from https://dot.net/'

Write-Step 'Publishing the Windows host'
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

$publishArgs = @(
    'publish', (Join-Path $hostDir 'FlowScreen.csproj'),
    '-c', $Configuration,
    '-r', 'win-x64',
    '--self-contained', 'false',
    '-o', $publishDir,
    '--nologo',
    '-v', 'minimal'
)

if ($SingleFile) {
    $publishArgs += @(
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=false',
        '-p:DebugType=none'
    )
}

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# --------------------------------------------------------------- artifacts --
Write-Step 'Assembling artifacts'
if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Output | Out-Null

Copy-Item -Path (Join-Path $publishDir '*') -Destination $Output -Recurse -Force

$exe = Join-Path $Output 'FlowScreen.exe'
if (-not (Test-Path $exe)) { throw "Expected FlowScreen.exe in $Output" }

$scr = Join-Path $Output 'FlowScreen.scr'
Copy-Item -Path $exe -Destination $scr -Force

Write-Host ''
Write-Host 'Build complete.' -ForegroundColor Green
Get-ChildItem $Output -File | Sort-Object Length -Descending | ForEach-Object {
    Write-Host ("  {0,-34} {1,9:N0} KB" -f $_.Name, ($_.Length / 1KB))
}

Write-Host ''
Write-Host 'Next:' -ForegroundColor White
Write-Host "  Preview settings   : `"$exe`" /c"
Write-Host "  Run full screen    : `"$scr`" /s"
Write-Host "  Install for the user: pwsh scripts/install.ps1"
