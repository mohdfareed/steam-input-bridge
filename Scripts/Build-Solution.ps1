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

$root = Resolve-Path "$PSScriptRoot\.."
$solution = Join-Path $root "SteamInputBridge.slnx"
$firmwareDirectory = Join-Path $root "SteamInputBridge.Firmware"

function Find-PlatformIO {
    $command = Get-Command "pio" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $extensionPath = Join-Path $env:USERPROFILE ".platformio\penv\Scripts\platformio.exe"
    if (Test-Path -LiteralPath $extensionPath) {
        return $extensionPath
    }

    throw "PlatformIO CLI was not found. Install PlatformIO or run the VS Code PlatformIO extension once."
}

Push-Location $root
try {
    if (-not $SkipDotNet) {
        if (-not $SkipFormat) {
            Write-Host "Formatting solution"
            dotnet format $solution
            if ($LASTEXITCODE -ne 0) {
                exit $LASTEXITCODE
            }
        }

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
