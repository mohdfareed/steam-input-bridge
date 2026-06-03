param(
    [string] $Configuration = "Debug",
    [string] $FirmwareEnvironment = "teensy40",
    [switch] $SkipFormat,
    [switch] $SkipDotNet,
    [switch] $SkipFirmware,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $DotNetBuildArgs
)

$ErrorActionPreference = "Stop"

. "$PSScriptRoot\Script-Helpers.ps1"

$root = Resolve-Path "$PSScriptRoot\.."
$solution = Join-Path $root "SteamInputBridge.slnx"
$firmwareDirectory = Join-Path $root "SteamInputBridge.Firmware"

Push-Location $root
try {
    if (-not $SkipFormat) {
        if (-not $SkipDotNet) {
            Write-Host "Formatting solution"
            dotnet format $solution
            if ($LASTEXITCODE -ne 0) {
                exit $LASTEXITCODE
            }
        }

        if (-not $SkipFirmware) {
            $clangFormat = Find-ClangFormat
            Format-Firmware -ClangFormat $clangFormat -FirmwareDirectory $firmwareDirectory
        }
    }

    if (-not $SkipDotNet) {
        $buildArgs = @("build", $solution, "--configuration", $Configuration)
        if ($DotNetBuildArgs) {
            $buildArgs += $DotNetBuildArgs
        }

        Write-Host "Building solution"
        & dotnet @buildArgs
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }

    if (-not $SkipFirmware) {
        Write-Host "Building Teensy firmware"
        $platformio = Find-PlatformIO
        & $platformio run -d $firmwareDirectory -e $FirmwareEnvironment --silent
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
}
finally {
    Pop-Location
}
