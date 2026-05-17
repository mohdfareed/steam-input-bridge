Push-Location "$PSScriptRoot\..\cli"
try {
    dotnet run -- @args
}
finally {
    Pop-Location
}
