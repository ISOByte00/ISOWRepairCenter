$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\ISOWRepairCenter\ISOWRepairCenter.csproj'
$output = Join-Path $PSScriptRoot 'dist\ISOWRepairCenter'

dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $output
if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE." }

Write-Host "Portable executable created: $(Join-Path $output 'ISOWRepairCenter.exe')"
