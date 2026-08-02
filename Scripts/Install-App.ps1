<#
.SYNOPSIS
Installs or updates Steam Input Bridge for the current user.

.DESCRIPTION
Use -Local from a cloned repository to build and install the app. Release
installation is not available yet.

.PARAMETER Local
Builds and installs from the repository containing this script.
#>
param(
    [switch] $Local
)

$ErrorActionPreference = "Stop"

# Validate the local installation source
# -----------------------------------------------------------------------------

if ($env:OS -ne "Windows_NT") {
    throw "Steam Input Bridge installation requires Windows."
}
if (-not $Local) {
    throw "Release installation is not available yet. Use -Local from a cloned repository."
}

$repositoryPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$deployScript = Join-Path $PSScriptRoot "Deploy-App.ps1"
$uninstallScript = Join-Path $PSScriptRoot "Uninstall-App.ps1"

if (-not (Test-Path -LiteralPath (Join-Path $repositoryPath ".git")) -or
    -not (Test-Path -LiteralPath $deployScript -PathType Leaf) -or
    -not (Test-Path -LiteralPath $uninstallScript -PathType Leaf)) {
    throw "This script is not inside a cloned Steam Input Bridge repository."
}

# Build the local deployment
# -----------------------------------------------------------------------------

& $deployScript
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$outputPath = Join-Path $repositoryPath "bin"
$sourceApp = Join-Path $outputPath "SteamInputBridge.App.exe"
if (-not (Test-Path -LiteralPath $sourceApp -PathType Leaf)) {
    throw "The deployment did not produce SteamInputBridge.App.exe."
}

# Resolve product metadata and the standard per-user install path
# -----------------------------------------------------------------------------

$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($sourceApp)
$productName = $versionInfo.ProductName
$displayName = $versionInfo.FileDescription
if ([string]::IsNullOrWhiteSpace($productName) -or [string]::IsNullOrWhiteSpace($displayName)) {
    throw "The app is missing product metadata."
}

$shellApplication = New-Object -ComObject Shell.Application
$userProgramsFolder = $shellApplication.NameSpace("shell:UserProgramFiles")
if (-not $userProgramsFolder) {
    throw "Windows' current-user Programs directory is unavailable."
}

$installPath = [System.IO.Path]::GetFullPath((Join-Path $userProgramsFolder.Self.Path $productName))
$installedApp = Join-Path $installPath "SteamInputBridge.App.exe"
$installedCli = Join-Path $installPath "SteamInputBridge.Cli.exe"
$commandPath = Join-Path $installPath "bin"

# Never recursively replace an unrelated directory at the product path.
if ((Test-Path -LiteralPath $installPath) -and
    @(Get-ChildItem -LiteralPath $installPath -Force).Count -ne 0 -and
    -not (Test-Path -LiteralPath $installedApp -PathType Leaf)) {
    throw "Refusing to replace a directory that is not a Steam Input Bridge installation: $installPath"
}

# Stop only processes running from this exact installation
# -----------------------------------------------------------------------------

foreach ($path in @($installedApp, $installedCli)) {
    $targetPath = [System.IO.Path]::GetFullPath($path)
    Get-Process -Name ([System.IO.Path]::GetFileNameWithoutExtension($path)) -ErrorAction SilentlyContinue |
    Where-Object {
        try { [string]::Equals($_.Path, $targetPath, [StringComparison]::OrdinalIgnoreCase) }
        catch { $false }
    } |
    Stop-Process -Force
}

# Install application files
# -----------------------------------------------------------------------------

if (Test-Path -LiteralPath $installPath) {
    Remove-Item -LiteralPath $installPath -Recurse -Force
}
New-Item -ItemType Directory -Path $installPath | Out-Null

# Local settings and logs belong to the development deployment, not the install.
Get-ChildItem -LiteralPath $outputPath -Force |
Where-Object { $_.Name -ne "appsettings.json" -and $_.Name -ne "logs" } |
Copy-Item -Destination $installPath -Recurse -Force

# Keep a standalone uninstaller beside the installed app.
$installedUninstaller = Join-Path $installPath "Uninstall-App.ps1"
Copy-Item -LiteralPath $uninstallScript -Destination $installedUninstaller

# Keep only the public command on PATH, not the app and uninstaller.
New-Item -ItemType Directory -Path $commandPath | Out-Null
@'
@echo off
"%~dp0..\SteamInputBridge.Cli.exe" %*
exit /b %errorlevel%
'@ | Set-Content -LiteralPath (Join-Path $commandPath "steam-input-bridge.cmd") -Encoding ascii

# Create the Start Menu shortcut
# -----------------------------------------------------------------------------

$shortcutPath = Join-Path `
([Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)) `
    "$displayName.lnk"
$shortcutShell = New-Object -ComObject WScript.Shell
$shortcut = $shortcutShell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $installedApp
$shortcut.WorkingDirectory = $installPath
$shortcut.IconLocation = "$installedApp,0"
$shortcut.Save()

# Install the shared VIIPER dependency when missing
# -----------------------------------------------------------------------------

$localApplicationData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$viiperPath = Join-Path $localApplicationData "VIIPER\viiper.exe"
if (-not (Test-Path -LiteralPath $viiperPath -PathType Leaf)) {
    Write-Host "Installing VIIPER"
    $viiperInstaller = Invoke-RestMethod "https://alia5.github.io/VIIPER/stable/install.ps1"
    & ([scriptblock]::Create([string] $viiperInstaller))
}
if (-not (Test-Path -LiteralPath $viiperPath -PathType Leaf)) {
    throw "VIIPER installation failed."
}

# Add the CLI command to the current user's PATH
# -----------------------------------------------------------------------------

$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
$commandPathEntry = $commandPath.TrimEnd([System.IO.Path]::DirectorySeparatorChar)
$hasCommandPath = @($userPath -split ";") | Where-Object {
    $entry = $_.Trim().Trim('"').TrimEnd([System.IO.Path]::DirectorySeparatorChar)
    [string]::Equals($entry, $commandPathEntry, [StringComparison]::OrdinalIgnoreCase)
}

if (-not $hasCommandPath) {
    $updatedUserPath = if ([string]::IsNullOrWhiteSpace($userPath)) {
        $commandPath
    }
    else {
        "$userPath;$commandPath"
    }
    [Environment]::SetEnvironmentVariable("Path", $updatedUserPath, "User")
}

# Start the installed app
# -----------------------------------------------------------------------------

Start-Process -FilePath $installedApp -WorkingDirectory $installPath
Write-Host "Installed $displayName to $installPath"
Write-Host "Uninstall with: $installedUninstaller"
