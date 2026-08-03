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
$commandPath = Join-Path $installPath "bin"
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

# Run independent cleanup actions and retain every failure for the final summary
# -----------------------------------------------------------------------------

$uninstallWarnings = [System.Collections.Generic.List[string]]::new()
function Invoke-UninstallAction {
    param(
        [string] $Description,
        [scriptblock] $Action
    )

    Write-Host $Description
    try {
        & $Action
    }
    catch {
        $warning = "$Description failed: $($_.Exception.Message)"
        $uninstallWarnings.Add($warning)
        Write-Warning $warning
    }
}

# Stop only processes running from this exact installation.
foreach ($path in @($installedApp, $installedCli)) {
    $targetPath = [System.IO.Path]::GetFullPath($path)
    $processName = [System.IO.Path]::GetFileNameWithoutExtension($path)
    Invoke-UninstallAction "Stopping $processName processes from $targetPath" {
        Get-Process -Name $processName -ErrorAction SilentlyContinue |
        Where-Object {
            try { [string]::Equals($_.Path, $targetPath, [StringComparison]::OrdinalIgnoreCase) }
            catch { $false }
        } |
        Stop-Process -Force
    }
}

# Remove Windows integration owned by this installation.
Invoke-UninstallAction "Removing startup registration" {
    $runKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey(
        "Software\Microsoft\Windows\CurrentVersion\Run",
        $true)
    try {
        if ($runKey) {
            $startupName = "$productName.Tray"
            if ($runKey.GetValue($startupName) -eq ('"' + $installedApp + '"')) {
                $runKey.DeleteValue($startupName, $false)
            }
        }
    }
    finally {
        if ($runKey) {
            $runKey.Dispose()
        }
    }
}

# Preserve a same-named shortcut if the user has pointed it somewhere else.
$shortcutPath = Join-Path `
([Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)) `
    "$displayName.lnk"
Invoke-UninstallAction "Removing Start Menu shortcut $shortcutPath" {
    if (Test-Path -LiteralPath $shortcutPath -PathType Leaf) {
        $shortcutShell = New-Object -ComObject WScript.Shell
        $shortcut = $shortcutShell.CreateShortcut($shortcutPath)
        try {
            if ([string]::Equals($shortcut.TargetPath, $installedApp, [StringComparison]::OrdinalIgnoreCase)) {
                Remove-Item -LiteralPath $shortcutPath -Force
            }
        }
        finally {
            [void] [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
            [void] [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcutShell)
        }
    }
}

# Remove only the CLI command directory owned by this installation.
Invoke-UninstallAction "Removing $commandPath from the user PATH" {
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $commandPathEntry = $commandPath.TrimEnd([System.IO.Path]::DirectorySeparatorChar)
    $updatedUserPath = (@($userPath -split ";") | Where-Object {
            $entry = $_.Trim().Trim('"').TrimEnd([System.IO.Path]::DirectorySeparatorChar)
            -not [string]::Equals($entry, $commandPathEntry, [StringComparison]::OrdinalIgnoreCase)
        }) -join ";"

    if (-not [string]::Equals($userPath, $updatedUserPath, [StringComparison]::Ordinal)) {
        [Environment]::SetEnvironmentVariable("Path", $updatedUserPath, "User")
    }
}

# Remove user data only when explicitly requested.
if ($Purge) {
    $dataPath = Join-Path `
    ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) `
        $productName
    Invoke-UninstallAction "Removing user settings and logs from $dataPath" {
        if (Test-Path -LiteralPath $dataPath) {
            Remove-Item -LiteralPath $dataPath -Recurse -Force
        }
    }
}

# Remove the installed application last.
Set-Location ([System.IO.Path]::GetTempPath())
Invoke-UninstallAction "Removing installed application from $installPath" {
    Remove-Item -LiteralPath $installPath -Recurse -Force
}

Write-Host # separator
if ($uninstallWarnings.Count -eq 0) {
    Write-Host "Uninstalled $displayName"
}
else {
    Write-Warning "Uninstall completed with $($uninstallWarnings.Count) warning(s):"
    foreach ($warning in $uninstallWarnings) {
        Write-Warning " - $warning"
    }
}
