# Tool discovery
# -----------------------------------------------------------------------------

# Prefer tools on PATH, then check the standard locations used by the Windows
# development tools supported by this repository.
function Find-ClangFormat {
    $command = Get-Command "clang-format" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    if ($IsMacOS) {
        $xcrun = Get-Command "xcrun" -ErrorAction SilentlyContinue
        if ($xcrun) {
            $tool = & $xcrun.Source --find clang-format 2>$null
            if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $tool -PathType Leaf)) {
                return $tool
            }
        }
    }

    $userProfilePath = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    $extensionRoots = @()
    if (-not [string]::IsNullOrWhiteSpace($userProfilePath)) {
        $extensionRoots += Join-Path $userProfilePath ".vscode\extensions"
        $extensionRoots += Join-Path $userProfilePath ".vscode-insiders\extensions"
    }
    foreach ($extensionRoot in $extensionRoots) {
        if (-not (Test-Path -LiteralPath $extensionRoot)) {
            continue
        }

        $extensionTools = @(
            Get-ChildItem -Path (Join-Path $extensionRoot "ms-vscode.cpptools-*") -Directory -ErrorAction SilentlyContinue |
            ForEach-Object { Join-Path $_.FullName "LLVM\bin\clang-format.exe" } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Sort-Object -Descending
        )
        if ($extensionTools.Count -gt 0) {
            return $extensionTools[0]
        }
    }

    foreach ($tool in @(
            "C:\Program Files\LLVM\bin\clang-format.exe"
            "C:\Program Files (x86)\LLVM\bin\clang-format.exe"
        )) {
        if (Test-Path -LiteralPath $tool) {
            return $tool
        }
    }

    $visualStudioPatterns = @(
        "C:\Program Files\Microsoft Visual Studio\*\*\VC\Tools\Llvm\bin\clang-format.exe"
        "C:\Program Files\Microsoft Visual Studio\*\*\VC\Tools\Llvm\*\bin\clang-format.exe"
        "C:\Program Files (x86)\Microsoft Visual Studio\*\*\VC\Tools\Llvm\bin\clang-format.exe"
        "C:\Program Files (x86)\Microsoft Visual Studio\*\*\VC\Tools\Llvm\*\bin\clang-format.exe"
    )
    foreach ($pattern in $visualStudioPatterns) {
        $visualStudioTools = @(
            Get-ChildItem -Path $pattern -File -ErrorAction SilentlyContinue |
            Sort-Object -Property FullName -Descending
        )
        if ($visualStudioTools.Count -gt 0) {
            return $visualStudioTools[0].FullName
        }
    }

    throw "clang-format was not found. Install clang-format or put it on PATH."
}

function Find-PlatformIO {
    $command = Get-Command "pio" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $userProfilePath = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    if (-not [string]::IsNullOrWhiteSpace($userProfilePath)) {
        $extensionPath = Join-Path $userProfilePath ".platformio\penv\Scripts\platformio.exe"
        if (Test-Path -LiteralPath $extensionPath -PathType Leaf) {
            return $extensionPath
        }
    }

    throw "PlatformIO CLI was not found. Install PlatformIO or run the VS Code PlatformIO extension once."
}

function Find-PlatformIOTeensyTools {
    $coreDirectories = @()
    if (-not [string]::IsNullOrWhiteSpace($env:PLATFORMIO_CORE_DIR)) {
        $coreDirectories += $env:PLATFORMIO_CORE_DIR
    }
    $userProfilePath = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    if (-not [string]::IsNullOrWhiteSpace($userProfilePath)) {
        $coreDirectories += Join-Path $userProfilePath ".platformio"
    }

    foreach ($coreDirectory in $coreDirectories) {
        $toolsDirectory = Join-Path $coreDirectory "packages\tool-teensy"
        if (Test-Path -LiteralPath (Join-Path $toolsDirectory "teensy_post_compile.exe")) {
            return [System.IO.Path]::GetFullPath($toolsDirectory)
        }
    }

    throw "PlatformIO Teensy upload tools were not found. Build the firmware once so PlatformIO installs tool-teensy."
}

# Formatting and firmware
# -----------------------------------------------------------------------------

function Format-Firmware {
    param(
        [string] $ClangFormat,
        [string] $FirmwareDirectory
    )

    $sourceFiles = @(
        Get-ChildItem -Path $FirmwareDirectory -Recurse -File -Include *.c, *.cpp, *.h, *.hpp |
        Where-Object { $_.FullName -notmatch "[\\/]\.pio[\\/]" } |
        Sort-Object -Property FullName
    )
    if ($sourceFiles.Count -eq 0) {
        return
    }

    Write-Host "Formatting Teensy firmware"
    $arguments = @("-i", "--style=file") + @($sourceFiles | Select-Object -ExpandProperty FullName)
    & $ClangFormat @arguments
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

function Build-Firmware {
    param(
        [string] $PlatformIO,
        [string] $FirmwareDirectory,
        [string] $FirmwareEnvironment = "teensy40"
    )

    Write-Host "Building Teensy firmware"
    & $PlatformIO run -d $FirmwareDirectory -e $FirmwareEnvironment --silent
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

# Deployment
# -----------------------------------------------------------------------------

function Deploy-Project {
    param(
        [string] $Configuration,
        [string] $Runtime,
        [string] $ProjectPath,
        [string] $OutputPath
    )

    # Publish projects independently so one project's publish cleanup cannot
    # remove files already produced by the other project.
    $publishPath = Join-Path `
    ([System.IO.Path]::GetTempPath()) `
        "SteamInputBridge.publish.$([System.Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $publishPath -Force | Out-Null

    try {
        Write-Host "Publishing $([System.IO.Path]::GetFileNameWithoutExtension($ProjectPath))"
        dotnet publish $ProjectPath `
            --configuration $Configuration `
            --runtime $Runtime `
            --output $publishPath `
            --self-contained true `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:EnableCompressionInSingleFile=true `
            -p:PublishDocumentationFile=false `
            -p:DebugType=embedded
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }

        New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
        Copy-Item -Path (Join-Path $publishPath "*") -Destination $OutputPath -Recurse -Force
    }
    finally {
        if (Test-Path -LiteralPath $publishPath) {
            Remove-Item -LiteralPath $publishPath -Recurse -Force
        }
    }

    Write-Host "Deployed $([System.IO.Path]::GetFileNameWithoutExtension($ProjectPath)) to $OutputPath"
}

function Deploy-Firmware {
    param(
        [string] $PlatformIO,
        [string] $FirmwareProject,
        [string] $FirmwareEnvironment,
        [string] $OutputPath
    )

    Build-Firmware `
        -PlatformIO $PlatformIO `
        -FirmwareDirectory $FirmwareProject `
        -FirmwareEnvironment $FirmwareEnvironment

    $firmwareSource = Join-Path $FirmwareProject ".pio\build\$FirmwareEnvironment\firmware.hex"
    if (-not (Test-Path -LiteralPath $firmwareSource -PathType Leaf)) {
        throw "Firmware build did not produce $firmwareSource"
    }

    Copy-Item `
        -LiteralPath $firmwareSource `
        -Destination (Join-Path $OutputPath "SteamInputBridge.Teensy.hex") `
        -Force
    Write-Host "Deployed Teensy firmware to $OutputPath"
}

function Copy-TeensyTools {
    param(
        [string] $SourcePath,
        [string] $OutputPath
    )

    $source = [System.IO.Path]::GetFullPath($SourcePath)
    $output = [System.IO.Path]::GetFullPath($OutputPath)
    $destination = [System.IO.Path]::GetFullPath((Join-Path $output "teensy"))

    if (-not (Test-Path -LiteralPath (Join-Path $source "teensy_post_compile.exe") -PathType Leaf)) {
        throw "PlatformIO Teensy upload tools are missing teensy_post_compile.exe: $source"
    }

    # The destination is deleted before copying, so keep that deletion strictly
    # beneath the selected deployment directory.
    $outputPrefix = $output.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $destination.StartsWith($outputPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to replace Teensy tools outside the deploy output: $destination"
    }

    if (Test-Path -LiteralPath $destination) {
        Write-Host "Removing existing Teensy upload tools"
        Remove-Item -LiteralPath $destination -Recurse -Force
    }

    Write-Host "Copying Teensy upload tools"
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -Path (Join-Path $source "*") -Destination $destination -Recurse -Force
    Write-Host "Deployed Teensy upload tools to $destination"
}

# Process lifecycle
# -----------------------------------------------------------------------------

function Stop-DeployedApp {
    param(
        [string] $Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return
    }

    $targetPath = [System.IO.Path]::GetFullPath($Path)
    $processName = [System.IO.Path]::GetFileNameWithoutExtension($Path)
    $processes = Get-Process -Name $processName -ErrorAction SilentlyContinue |
    Where-Object {
        try {
            [string]::Equals($_.Path, $targetPath, [System.StringComparison]::OrdinalIgnoreCase)
        }
        catch {
            $false
        }
    }

    foreach ($process in $processes) {
        Write-Host "Stopping $processName ($($process.Id))"
        Stop-Process -Id $process.Id -Force
        $null = $process.WaitForExit(5000)
    }
}

function Start-DeployedApp {
    param(
        [string] $Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "The deployed application does not exist: $Path"
    }

    Start-Process `
        -FilePath $Path `
        -WorkingDirectory (Split-Path -Parent $Path)
    Write-Host "Started Steam Input Bridge"
}
