Push-Location "$PSScriptRoot\..\app"
try {
    dotnet run --project .\VirtualMouse.csproj -- @args
}
finally {
    Pop-Location
}
