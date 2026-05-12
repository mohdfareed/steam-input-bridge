param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "..\tests\PhysicalMouse.Tests\PhysicalMouse.Tests.csproj"

dotnet test $project --configuration $Configuration
