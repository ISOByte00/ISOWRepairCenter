$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\ISOWRepairCenter\ISOWRepairCenter.csproj'
$output = Join-Path $PSScriptRoot 'dist\ISOWRepairCenter'
$version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
$archive = Join-Path $PSScriptRoot "dist\ISOWRepairCenter-$version-win-x64.zip"
$checksum = "$archive.sha256"

dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $output
if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE." }

Write-Host "Portable executable created: $(Join-Path $output 'ISOWRepairCenter.exe')"
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $archive -Force
$hash = Get-FileHash -LiteralPath $archive -Algorithm SHA256
Set-Content -LiteralPath $checksum -Value "$($hash.Hash)  $(Split-Path -Leaf $archive)" -NoNewline
Write-Host "Portable archive created: $archive"
Write-Host "SHA-256 file: $checksum"
Write-Host "Archive SHA-256: $($hash.Hash)"
