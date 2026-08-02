<#
.SYNOPSIS
Runs the Steam Input Bridge test project.
#>

$ErrorActionPreference = "Stop"

# Resolve the project from the script location so this works from any directory.
$testProject = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot "..\SteamInputBridge.Tests\SteamInputBridge.Tests.csproj"))

Write-Host "Running tests"
dotnet test $testProject
exit $LASTEXITCODE
