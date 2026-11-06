# One-click installer for Boomerang Foo: Chaos Mod (remix).
# Downloads the latest source of a branch from GitHub, builds it against your Boomerang Fu install,
# and copies the mod into BepInEx/plugins/BoomerangFoo. Normally started by Install-BoomerangFoo.bat.
# Written for Windows PowerShell 5.1.

param(
    [string]$Branch = 'master',
    [string]$GamePath = ''
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 downloads extremely slowly while drawing its progress bar
$ProgressPreference = 'SilentlyContinue'
$Repo = 'alialyami90/BoomerangFooRemix'
$SteamAppId = '965680'
$ConfigDir = Join-Path $env:APPDATA 'BoomerangFooRemix'
$ConfigFile = Join-Path $ConfigDir 'install-config.json'
$WorkDir = Join-Path $env:TEMP 'BoomerangFooRemix-build'
$LogFile = Join-Path $env:TEMP 'BoomerangFooRemix-install.log'

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Write-Step([string]$message) { Write-Host "`n==> $message" -ForegroundColor Cyan }
function Fail([string]$message) {
    Write-Host "`nINSTALL FAILED: $message" -ForegroundColor Red
    Write-Host "Full log: $LogFile"
    try { Stop-Transcript | Out-Null } catch {}
    exit 1
}

function Test-GameFolder([string]$path) {
    return $path -and (Test-Path -LiteralPath (Join-Path $path 'Boomerang Fu.exe'))
}

# Finds Boomerang Fu through Steam's library list (covers games on other drives, e.g. G:\SteamLibrary)
function Find-GameFolder {
    $steamRoots = @()
    foreach ($key in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam')) {
        $props = Get-ItemProperty -Path $key -ErrorAction SilentlyContinue
        if ($props) {
            if ($props.SteamPath) { $steamRoots += $props.SteamPath }
            if ($props.InstallPath) { $steamRoots += $props.InstallPath }
        }
    }
    $steamRoots += 'C:\Program Files (x86)\Steam'

    $libraries = @()
    foreach ($root in ($steamRoots | Select-Object -Unique)) {
        $root = $root -replace '/', '\'
        $libraries += $root
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $vdf) {
            foreach ($match in [regex]::Matches((Get-Content -LiteralPath $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $libraries += ($match.Groups[1].Value -replace '\\\\', '\')
            }
        }
    }

    foreach ($library in ($libraries | Select-Object -Unique)) {
        $candidate = Join-Path $library 'steamapps\common\Boomerang Fu'
        if (Test-GameFolder $candidate) { return $candidate }
    }
    return $null
}

function Get-SavedGamePath {
    if (Test-Path -LiteralPath $ConfigFile) {
        try { return (Get-Content -LiteralPath $ConfigFile -Raw | ConvertFrom-Json).GamePath } catch {}
    }
    return $null
}

function Save-GamePath([string]$path) {
    New-Item -ItemType Directory -Force -Path $ConfigDir | Out-Null
    @{ GamePath = $path } | ConvertTo-Json | Set-Content -LiteralPath $ConfigFile -Encoding UTF8
}

Start-Transcript -Path $LogFile -Force | Out-Null
Write-Host "Boomerang Foo installer - branch '$Branch'" -ForegroundColor Green

# 1. .NET SDK
Write-Step 'Checking for the .NET SDK'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Fail 'The .NET SDK is not installed. Install it from https://dotnet.microsoft.com/download (version 8 or newer), then run this again.'
}
Write-Host "Found dotnet $(& dotnet --version)"

# 2. Game folder: explicit, saved from last time, auto-detected, or asked once
Write-Step 'Finding Boomerang Fu'
if (-not (Test-GameFolder $GamePath)) { $GamePath = Get-SavedGamePath }
if (-not (Test-GameFolder $GamePath)) { $GamePath = Find-GameFolder }
while (-not (Test-GameFolder $GamePath)) {
    $GamePath = (Read-Host 'Could not find Boomerang Fu. Paste the game folder (the one containing "Boomerang Fu.exe")').Trim([char[]]@('"', ' '))
}
Save-GamePath $GamePath
Write-Host "Game folder: $GamePath"

# 3. The game locks the mod DLL while running
$running = Get-Process -Name 'Boomerang Fu' -ErrorAction SilentlyContinue
if ($running) {
    $answer = Read-Host 'Boomerang Fu is running. Close it and continue? (Y/N)'
    if ($answer -notmatch '^[Yy]') { Fail 'Close Boomerang Fu, then run the installer again.' }
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 30 -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
}

# 4. Download the latest source of the branch
Write-Step "Downloading the latest '$Branch' from GitHub"
if (Test-Path -LiteralPath $WorkDir) { Remove-Item -LiteralPath $WorkDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
$zip = Join-Path $WorkDir 'source.zip'
try {
    Invoke-WebRequest -UseBasicParsing -TimeoutSec 300 -Uri "https://github.com/$Repo/archive/refs/heads/$Branch.zip" -OutFile $zip
} catch {
    Fail "Could not download branch '$Branch' from github.com/$Repo. Check your internet connection and that the branch exists. ($($_.Exception.Message))"
}
Expand-Archive -LiteralPath $zip -DestinationPath $WorkDir -Force
$project = Get-ChildItem -LiteralPath $WorkDir -Recurse -Filter 'BoomerangFoo.csproj' | Select-Object -First 1
if (-not $project) { Fail 'The download did not contain src/BoomerangFoo.csproj.' }

$version = ''
try {
    $commit = Invoke-RestMethod -UseBasicParsing -TimeoutSec 20 -Uri "https://api.github.com/repos/$Repo/commits/$Branch"
    $version = "$($commit.sha.Substring(0, 7)) - $(($commit.commit.message -split "`n")[0])"
    Write-Host "Latest change: $version"
} catch {}

# 5. Build; the project's own build step copies the DLLs into the game (and installs BepInEx if missing)
Write-Step 'Building and installing the mod (the first run can take a few minutes)'
$started = Get-Date
& dotnet build $project.FullName -c Release "-p:GamePath=$GamePath" -p:SkipReleaseZip=true -nologo -v:minimal
if ($LASTEXITCODE -ne 0) { Fail 'The build failed. The errors are listed above (red "error" lines).' }

# 6. Check the installed DLL really was replaced
$installed = Join-Path $GamePath 'BepInEx\plugins\BoomerangFoo\BoomerangFoo.dll'
if (-not (Test-Path -LiteralPath $installed) -or (Get-Item -LiteralPath $installed).LastWriteTime -lt $started.AddMinutes(-1)) {
    Fail "The build finished but $installed was not updated."
}

Write-Host "`nINSTALLED SUCCESSFULLY" -ForegroundColor Green
if ($version) { Write-Host "Version: $version" }
Write-Host "Mod file: $installed"
Write-Host "You can start Boomerang Fu now (Steam app $SteamAppId)."
try { Stop-Transcript | Out-Null } catch {}
