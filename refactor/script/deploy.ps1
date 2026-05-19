param(
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [string] $Output = "$PSScriptRoot\..\deploy"
)

$ErrorActionPreference = "Stop"

$appProject = Resolve-Path "$PSScriptRoot\..\app\VirtualMouse.csproj"
$outputPath = [System.IO.Path]::GetFullPath($Output)

if (Test-Path $outputPath) {
    Remove-Item -LiteralPath $outputPath -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

dotnet publish $appProject `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $outputPath `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishDocumentationFile=false `
    -p:AllowedReferenceRelatedFileExtensions=.pdb `
    -p:DebugType=embedded

Write-Host "Deployed VirtualMouse to $outputPath"
