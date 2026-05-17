Push-Location "$PSScriptRoot\.."
try {
    dotnet test ".\tests\Communication.Tests\Communication.Tests.csproj" @args
}
finally {
    Pop-Location
}
