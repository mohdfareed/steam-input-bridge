param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "..\src\PhysicalMouse\PhysicalMouse.csproj"
$output = Join-Path $PSScriptRoot "..\artifacts\packages"

dotnet pack $project --configuration $Configuration --output $output
