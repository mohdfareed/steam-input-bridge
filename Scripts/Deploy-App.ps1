<#
.SYNOPSIS
Publishes the app, CLI, Teensy firmware, and uploader into one deployment directory.

.PARAMETER Configuration
The .NET publish configuration. Defaults to Release.

.PARAMETER Runtime
The .NET runtime identifier. Defaults to win-x64.

.PARAMETER FirmwareEnvironment
The PlatformIO firmware environment packaged with the deployment. Defaults to teensy40.

.PARAMETER Output
The deployment directory. Defaults to the repository's bin directory.

.PARAMETER Start
Starts the deployed app after publishing.
#>
param(
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [string] $FirmwareEnvironment = "teensy40",
    [string] $Output = "$PSScriptRoot\..\bin",
    [switch] $Start
)

$ErrorActionPreference = "Stop"

. "$PSScriptRoot\Script-Helpers.ps1"

# Repository and deployment paths
# -----------------------------------------------------------------------------

$appProject = Resolve-Path "$PSScriptRoot\..\SteamInputBridge.App\SteamInputBridge.App.csproj"
$cliProject = Resolve-Path "$PSScriptRoot\..\SteamInputBridge.Cli\SteamInputBridge.Cli.csproj"
$firmwareProject = Resolve-Path "$PSScriptRoot\..\SteamInputBridge.Firmware"
$outputPath = [System.IO.Path]::GetFullPath($Output)
$appPath = Join-Path $outputPath "SteamInputBridge.App.exe"
$cliPath = Join-Path $outputPath "SteamInputBridge.Cli.exe"

# Stop the existing deployment
# -----------------------------------------------------------------------------

Stop-DeployedApp -Path $appPath
Stop-DeployedApp -Path $cliPath

# Publish application executables
# -----------------------------------------------------------------------------

Deploy-Project `
    -Configuration $Configuration `
    -Runtime $Runtime `
    -ProjectPath $appProject `
    -OutputPath $outputPath

Deploy-Project `
    -Configuration $Configuration `
    -Runtime $Runtime `
    -ProjectPath $cliProject `
    -OutputPath $outputPath

# Package Teensy firmware and uploader
# -----------------------------------------------------------------------------

Deploy-Firmware `
    -PlatformIO (Find-PlatformIO) `
    -FirmwareProject $firmwareProject `
    -FirmwareEnvironment $FirmwareEnvironment `
    -OutputPath $outputPath

Copy-TeensyTools `
    -SourcePath (Find-PlatformIOTeensyTools) `
    -OutputPath $outputPath

# Start the deployment
# -----------------------------------------------------------------------------

if ($Start) {
    Start-DeployedApp -Path $appPath
}
