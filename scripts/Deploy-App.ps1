param(
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [string] $Output = "$PSScriptRoot\..\bin",
    [switch] $Start
)

$ErrorActionPreference = "Stop"

$appProject = Resolve-Path "$PSScriptRoot\..\SteamInputBridge.App\SteamInputBridge.App.csproj"
$outputPath = [System.IO.Path]::GetFullPath($Output)
$exePath = Join-Path $outputPath "SteamInputBridge.exe"

function Stop-DeployedApp {
    param(
        [string] $Path
    )

    if (-not (Test-Path $Path)) {
        return
    }

    $targetPath = [System.IO.Path]::GetFullPath($Path)
    $processes = Get-Process -Name "SteamInputBridge" -ErrorAction SilentlyContinue | Where-Object {
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
        $process.WaitForExit(5000)
    }
}

Stop-DeployedApp $exePath

if (Test-Path $outputPath) {
    Remove-Item -LiteralPath $outputPath -Recurse -Force
}

dotnet publish $appProject `
    --configuration $Configuration `
    --runtime $Runtime `
    --output $outputPath `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishDocumentationFile=false `
    -p:DebugType=embedded

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Deployed SteamInputBridge to $outputPath"

if ($Start) {
    Start-Process -FilePath $exePath -WorkingDirectory $outputPath -WindowStyle Hidden
    Write-Host "Started SteamInputBridge"
}
