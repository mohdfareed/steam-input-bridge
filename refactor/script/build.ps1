$ErrorActionPreference = "Stop"

Push-Location "$PSScriptRoot\.."
try {
    dotnet format ".\Refactor.slnx"
    dotnet build ".\Refactor.slnx" -- @args
}
finally {
    Pop-Location
}
