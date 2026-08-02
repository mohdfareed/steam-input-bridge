<#
.SYNOPSIS
Uninstalls Steam Input Bridge for the current user.

.DESCRIPTION
Run this script from either a cloned repository or beside the installed app.
Settings and logs are preserved unless -Purge is specified.

.PARAMETER Purge
Also removes the default user settings and logs.
#>
param(
    [switch] $Purge
)

$ErrorActionPreference = "Stop"

# Resolve the product and installed application directory
# -----------------------------------------------------------------------------

if ($env:OS -ne "Windows_NT") {
    throw "Steam Input Bridge uninstallation requires Windows."
}

$scriptPath = [System.IO.Path]::GetFullPath($PSScriptRoot)
$repositoryPath = [System.IO.Path]::GetFullPath((Join-Path $scriptPath ".."))
$propertiesPath = Join-Path $repositoryPath "Directory.Build.props"
$isRepositoryCopy = (Test-Path -LiteralPath (Join-Path $repositoryPath ".git")) -and
    (Test-Path -LiteralPath $propertiesPath -PathType Leaf)

if ($isRepositoryCopy) {
    # Directory.Build.props is the source of the product metadata used by builds.
    [xml] $buildProperties = Get-Content -LiteralPath $propertiesPath -Raw
    $expectedProductName = [string] $buildProperties.Project.PropertyGroup.Product
}
else {
    $adjacentApp = Join-Path $scriptPath "SteamInputBridge.App.exe"
    if (-not (Test-Path -LiteralPath $adjacentApp -PathType Leaf)) {
        throw "Run Uninstall-App.ps1 from a cloned repository or beside the installed app."
    }

    $expectedProductName = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($adjacentApp).ProductName
}

if ([string]::IsNullOrWhiteSpace($expectedProductName)) {
    throw "The app is missing product metadata."
}

$shellApplication = New-Object -ComObject Shell.Application
$userProgramsFolder = $shellApplication.NameSpace("shell:UserProgramFiles")
if (-not $userProgramsFolder) {
    throw "Windows' current-user Programs directory is unavailable."
}

$expectedInstallPath = [System.IO.Path]::GetFullPath((Join-Path $userProgramsFolder.Self.Path $expectedProductName))
$installPath = if ($isRepositoryCopy) { $expectedInstallPath } else { $scriptPath }
if (-not [string]::Equals($installPath, $expectedInstallPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw "The uninstaller is not running from the installed application directory."
}

$installedApp = Join-Path $installPath "SteamInputBridge.App.exe"
$installedCli = Join-Path $installPath "SteamInputBridge.Cli.exe"
if (-not (Test-Path -LiteralPath $installedApp -PathType Leaf)) {
    throw "$expectedProductName is not installed."
}

$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($installedApp)
$productName = $versionInfo.ProductName
$displayName = $versionInfo.FileDescription
if ([string]::IsNullOrWhiteSpace($displayName) -or
    -not [string]::Equals($productName, $expectedProductName, [StringComparison]::Ordinal)) {
    throw "The installed app's product metadata does not match this uninstaller."
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

# Remove Windows integration owned by this installation
# -----------------------------------------------------------------------------

$runKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey(
    "Software\Microsoft\Windows\CurrentVersion\Run",
    $true)
if ($runKey) {
    $startupName = "$productName.Tray"
    if ($runKey.GetValue($startupName) -eq ('"' + $installedApp + '"')) {
        $runKey.DeleteValue($startupName, $false)
    }
    $runKey.Dispose()
}

# Preserve a same-named shortcut if the user has pointed it somewhere else.
$shortcutPath = Join-Path `
([Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)) `
    "$displayName.lnk"
if (Test-Path -LiteralPath $shortcutPath -PathType Leaf) {
    $shortcutShell = New-Object -ComObject WScript.Shell
    $shortcut = $shortcutShell.CreateShortcut($shortcutPath)
    if ([string]::Equals($shortcut.TargetPath, $installedApp, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $shortcutPath -Force
    }
}

# Remove user data only when explicitly requested
# -----------------------------------------------------------------------------

if ($Purge) {
    $dataPath = Join-Path `
    ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) `
        $productName
    if (Test-Path -LiteralPath $dataPath) {
        Remove-Item -LiteralPath $dataPath -Recurse -Force
    }
}

# Remove the installed application
# -----------------------------------------------------------------------------

Set-Location ([System.IO.Path]::GetTempPath())
Remove-Item -LiteralPath $installPath -Recurse -Force
Write-Host "Uninstalled $displayName"
