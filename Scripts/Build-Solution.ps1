<#
.SYNOPSIS
Formats and builds the .NET solution and Teensy firmware.

.PARAMETER Configuration
The .NET build configuration. Defaults to Debug.

.PARAMETER FirmwareEnvironment
The PlatformIO firmware environment. Defaults to teensy40.
#>
param(
    [string] $Configuration = "Debug",
    [string] $FirmwareEnvironment = "teensy40"
)

$ErrorActionPreference = "Stop"

. "$PSScriptRoot\Script-Helpers.ps1"

# Repository paths
# -----------------------------------------------------------------------------

$repositoryPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$solutionPath = Join-Path $repositoryPath "SteamInputBridge.slnx"
$firmwarePath = Join-Path $repositoryPath "SteamInputBridge.Firmware"

# Format source
# -----------------------------------------------------------------------------

Write-Host "Formatting .NET solution"
dotnet format $solutionPath
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Format-Firmware `
    -ClangFormat (Find-ClangFormat) `
    -FirmwareDirectory $firmwarePath

# Build outputs
# -----------------------------------------------------------------------------

Write-Host "Building .NET solution ($Configuration)"
dotnet build $solutionPath --configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Build-Firmware `
    -PlatformIO (Find-PlatformIO) `
    -FirmwareDirectory $firmwarePath `
    -FirmwareEnvironment $FirmwareEnvironment
