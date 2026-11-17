param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string]$LegacyDataDirectory
)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration
$projects = @('ApiStudio.SmokeTests', 'ApiStudio.WpfSmokeTests')
$outputs = @('core', 'wpf')
for ($index = 0; $index -lt $projects.Count; $index++) {
    $testProject = Join-Path $PSScriptRoot "tests\$($projects[$index])\$($projects[$index]).csproj"
    & dotnet build $testProject --configuration $Configuration --nologo -p:CopyRetryCount=0
    if ($LASTEXITCODE -ne 0) { throw "Test compilation failed: $($projects[$index])" }
    $testExe = Join-Path $PSScriptRoot "artifacts\tests\$($outputs[$index])\$($projects[$index]).exe"
    if ($index -eq 0 -and $LegacyDataDirectory) { & $testExe $LegacyDataDirectory }
    else { & $testExe }
    if ($LASTEXITCODE -ne 0) { throw "Tests failed: $($projects[$index])" }
}
