function Find-ClangFormat {
    $command = Get-Command "clang-format" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $extensionRoots = @(
        Join-Path $env:USERPROFILE ".vscode\extensions"
        Join-Path $env:USERPROFILE ".vscode-insiders\extensions"
    )

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

    $llvmTools = @(
        "C:\Program Files\LLVM\bin\clang-format.exe"
        "C:\Program Files (x86)\LLVM\bin\clang-format.exe"
    )
    foreach ($tool in $llvmTools) {
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

    throw "clang-format was not found. Install clang-format, install the VS Code PlatformIO or C++ extension, or put clang-format on PATH."
}

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

function Find-PlatformIOTeensyTools {
    $coreDirectories = @()
    if (-not [string]::IsNullOrWhiteSpace($env:PLATFORMIO_CORE_DIR)) {
        $coreDirectories += $env:PLATFORMIO_CORE_DIR
    }

    $coreDirectories += Join-Path $env:USERPROFILE ".platformio"
    foreach ($coreDirectory in $coreDirectories) {
        $toolsDirectory = Join-Path $coreDirectory "packages\tool-teensy"
        $uploader = Join-Path $toolsDirectory "teensy_post_compile.exe"
        if (Test-Path -LiteralPath $uploader) {
            return [System.IO.Path]::GetFullPath($toolsDirectory)
        }
    }

    throw "PlatformIO Teensy upload tools were not found. Build the firmware once with PlatformIO so it installs tool-teensy."
}

function Format-Firmware {
    param(
        [string] $ClangFormat,
        [string] $FirmwareDirectory
    )

    $sourceFiles = @(
        Get-ChildItem -Path $FirmwareDirectory -Recurse -File -Include *.c, *.cpp, *.h, *.hpp |
        Where-Object { $_.FullName -notmatch "\\\.pio\\" } |
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

function Copy-TeensyTools {
    param(
        [string] $SourcePath,
        [string] $OutputPath
    )

    $source = [System.IO.Path]::GetFullPath($SourcePath)
    $output = [System.IO.Path]::GetFullPath($OutputPath)
    $destination = [System.IO.Path]::GetFullPath((Join-Path $output "teensy"))

    if (-not (Test-Path -LiteralPath (Join-Path $source "teensy_post_compile.exe"))) {
        throw "PlatformIO Teensy upload tools are missing teensy_post_compile.exe: $source"
    }

    $outputPrefix = $output.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    if (-not $destination.StartsWith($outputPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to replace Teensy tools outside the deploy output: $destination"
    }

    if (Test-Path -LiteralPath $destination) {
        Remove-Item -LiteralPath $destination -Recurse -Force
    }

    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -Path (Join-Path $source "*") -Destination $destination -Recurse -Force
    Write-Host "Deployed Teensy upload tools to $destination"
}

function Deploy-Project {
    param(
        [string] $Configuration,
        [string] $Runtime,
        [string] $ProjectPath,
        [string] $OutputPath
    )

    $publishPath = Join-Path ([System.IO.Path]::GetTempPath()) "SteamInputBridge.publish.$([System.Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $publishPath -Force | Out-Null

    try {
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
        if (Test-Path $publishPath) {
            Remove-Item -LiteralPath $publishPath -Recurse -Force
        }
    }

    Write-Host "Deployed $([System.IO.Path]::GetFileNameWithoutExtension($ProjectPath)) to $OutputPath"
}

function Start-DeployedApp {
    param(
        [string] $Path
    )

    if (-not (Test-Path $Path)) {
        Write-Error "The specified path does not exist: $Path"
        return
    }

    $paths = Split-Path $Path
    Start-Process -FilePath $Path -WorkingDirectory $paths -WindowStyle Hidden
    Write-Host "Started SteamInputBridge"
}

function Stop-DeployedApp {
    param(
        [string] $Path
    )

    if (-not (Test-Path $Path)) {
        return
    }

    $targetPath = [System.IO.Path]::GetFullPath($Path)
    $processes = Get-Process `
        -Name "SteamInputBridge.App" `
        -ErrorAction SilentlyContinue | `
        Where-Object {
        try {
            $_.MainModule.FileName -eq $targetPath
        }
        catch {
            $false
        }
    }

    foreach ($process in $processes) {
        Write-Host "Stopping SteamInputBridge ($($process.Id))"
        Stop-Process -Id $process.Id -Force
        $null = $process.WaitForExit(5000)
    }
}

function Deploy-Firmware {
    param(
        [string] $BuildScriptPath,
        [string] $FirmwareProject,
        [string] $FirmwareEnvironment,
        [string] $OutputPath
    )

    & $BuildScriptPath -SkipDotNet -FirmwareEnvironment $FirmwareEnvironment
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    $firmwareSource = Join-Path $FirmwareProject ".pio\build\$FirmwareEnvironment\firmware.hex"
    if (-not (Test-Path -LiteralPath $firmwareSource)) {
        throw "Firmware build did not produce $firmwareSource"
    }

    Copy-Item `
        -LiteralPath $firmwareSource `
        -Destination (Join-Path $OutputPath "SteamInputBridge.Teensy.hex") `
        -Force
}
