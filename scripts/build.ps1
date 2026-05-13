param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$SkipFormat
)

$ErrorActionPreference = "Stop"

$solution = Join-Path $PSScriptRoot "..\virtual-mouse.slnx"

if (-not $SkipFormat)
{
    dotnet format $solution
}

dotnet build $solution --configuration $Configuration
