$ErrorActionPreference = "Stop"

Push-Location "$PSScriptRoot\.."

try {
    dotnet run --project .\SteamInputBridge.Rewrite.Cli\SteamInputBridge.Cli.csproj -- @args
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
