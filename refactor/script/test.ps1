Push-Location "$PSScriptRoot\.."
try {
    dotnet test ".\tests\VirtualMouse.Tests.csproj" @args
}
finally {
    Pop-Location
}
