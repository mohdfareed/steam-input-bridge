Push-Location "$PSScriptRoot\.."

try {
    dotnet run --project .\SteamInputBridge.App\SteamInputBridge.App.csproj -- @args
}
finally {
    Pop-Location
}
