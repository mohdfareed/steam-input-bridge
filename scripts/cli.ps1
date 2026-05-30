Push-Location "$PSScriptRoot\.."

try {
    dotnet run --project .\SteamInputBridge.Rewrite.App\SteamInputBridge.App.csproj -- @args
}
finally {
    Pop-Location
}
