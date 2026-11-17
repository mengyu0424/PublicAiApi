param([string]$Destination)
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path -Parent $PSScriptRoot
if (-not $Destination) { $Destination = Join-Path $repoPath 'ApiStudio\Assets' }
$projectPath = Join-Path $PSScriptRoot 'BrandAssets.csproj'
& dotnet build $projectPath --configuration Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Logo generator compilation failed.' }
$generatorPath = Join-Path $repoPath 'artifacts\branding\GenerateBrandAssets.exe'
& $generatorPath $Destination
if ($LASTEXITCODE -ne 0) { throw 'Logo generation failed.' }