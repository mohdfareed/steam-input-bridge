param(
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [string] $Output = "$PSScriptRoot\..\bin",
    [switch] $Start
)

$ErrorActionPreference = "Stop"

. "$PSScriptRoot\Script-Helpers.ps1"

$appProject = Resolve-Path "$PSScriptRoot\..\SteamInputBridge.App\SteamInputBridge.App.csproj"
$cliProject = Resolve-Path "$PSScriptRoot\..\SteamInputBridge.Cli\SteamInputBridge.Cli.csproj"
$firmwareProject = Resolve-Path "$PSScriptRoot\..\SteamInputBridge.Firmware"
$buildScript = Join-Path $PSScriptRoot "Build-Solution.ps1"
$teensyTools = Find-PlatformIOTeensyTools

$outputPath = [System.IO.Path]::GetFullPath($Output)
$appExePath = Join-Path $outputPath "SteamInputBridge.App.exe"
$cliExePath = Join-Path $outputPath "SteamInputBridge.Cli.exe"

Stop-DeployedApp -Path $appExePath
Stop-DeployedApp -Path $cliExePath

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

Deploy-Firmware `
    -BuildScriptPath $buildScript `
    -FirmwareProject $firmwareProject `
    -FirmwareEnvironment teensy40 `
    -OutputPath $outputPath
Copy-TeensyTools -SourcePath $teensyTools -OutputPath $outputPath

if ($Start) {
    Start-DeployedApp -Path $appExePath
}
