param(
    [ValidateSet("Normal", "Dependency", "Manual", "All")]
    [string]$Tier = "Normal"
)

$ErrorActionPreference = "Stop"

Push-Location "$PSScriptRoot\.."

try {
    $testArgs = @("test", ".\SteamInputBridge.Tests\SteamInputBridge.Tests.csproj")
    switch ($Tier) {
        "Normal" { $testArgs += @("--filter", "TestCategory!=Dependency&TestCategory!=Manual") }
        "Dependency" { $testArgs += @("--filter", "TestCategory=Dependency&TestCategory!=Manual") }
        "Manual" { $testArgs += @("--filter", "TestCategory=Manual") }
        "All" { }
    }

    $testArgs += $args
    & dotnet @testArgs
}
finally {
    Pop-Location
}
