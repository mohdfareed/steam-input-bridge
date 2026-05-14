param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$project = Resolve-Path (Join-Path $PSScriptRoot "..\tools\SteamInput.TestBench.Launcher\SteamInput.TestBench.Launcher.csproj")
$output = Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..")) "artifacts\steam-testbench"

dotnet publish $project --configuration $Configuration --runtime win-x64 --self-contained false --output $output

Write-Host "Steam shortcut target:"
Write-Host (Join-Path (Resolve-Path $output) "SteamInput.TestBench.Launcher.exe")
