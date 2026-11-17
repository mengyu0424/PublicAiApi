param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'ApiStudio\ApiStudio.csproj'
& dotnet build $projectPath --configuration $Configuration --nologo -p:CopyRetryCount=0
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Host "Build complete: $PSScriptRoot\ApiStudio\bin\$Configuration\ApiStudio.exe"