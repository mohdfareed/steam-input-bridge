param(
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [string] $Output = "$PSScriptRoot\..\bin",
    [switch] $Start
)

$ErrorActionPreference = "Stop"

$appProject = Resolve-Path "$PSScriptRoot\..\SteamInputBridge.App\SteamInputBridge.App.csproj"
$cliProject = Resolve-Path "$PSScriptRoot\..\SteamInputBridge.Cli\SteamInputBridge.Cli.csproj"

$outputPath = [System.IO.Path]::GetFullPath($Output)
$appExePath = Join-Path $outputPath "SteamInputBridge.App.exe"
$cliExePath = Join-Path $outputPath "SteamInputBridge.Cli.exe"

# MARK: Functions
# =============================================================================

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

# MARK: Entry Point
# =============================================================================

Stop-DeployedApp -Path $appExePath
Stop-DeployedApp -Path $cliExePath

Deploy-Project `
    -Configuration $Configuration `
    -Runtime $Runtime `
    -ProjectPath $appProject `
    -OutputPath $outputPath

Deploy-Project `
    -Configuration $Configuration `
    -Runtime $Runtime `
    -ProjectPath $cliProject `
    -OutputPath $outputPath

if ($Start) {
    Start-DeployedApp -Path $appExePath
}
