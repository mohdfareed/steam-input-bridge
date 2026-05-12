param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "..\src\PhysicalMouse\PhysicalMouse.csproj"

dotnet build $project --configuration $Configuration
