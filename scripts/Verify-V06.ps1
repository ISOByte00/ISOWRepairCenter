$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'ISOWRepairCenter.sln'
$publishScript = Join-Path $root 'Publish-Portable.ps1'

function Invoke-DotNet([string[]] $Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

Push-Location $root
try {
    Invoke-DotNet @('restore', $solution)
    Invoke-DotNet @('build', $solution, '-c', 'Release', '--no-restore')
    Invoke-DotNet @('test', $solution, '-c', 'Release', '--no-build')

    & $publishScript
    if ($LASTEXITCODE -ne 0) { throw "Publish-Portable.ps1 failed with exit code $LASTEXITCODE." }

    $exe = Join-Path $root 'dist\ISOWRepairCenter\ISOWRepairCenter.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw "Portable executable was not found: $exe" }
    $version = ([xml](Get-Content -LiteralPath (Join-Path $root 'src\ISOWRepairCenter\ISOWRepairCenter.csproj') -Raw)).Project.PropertyGroup.Version
    $archive = Join-Path $root "dist\ISOWRepairCenter-$version-win-x64.zip"
    $checksum = "$archive.sha256"
    if (-not (Test-Path -LiteralPath $archive)) { throw "Portable archive was not found: $archive" }
    if (-not (Test-Path -LiteralPath $checksum)) { throw "Portable archive checksum was not found: $checksum" }
    $hash = Get-FileHash -LiteralPath $exe -Algorithm SHA256
    $archiveHash = Get-FileHash -LiteralPath $archive -Algorithm SHA256
    Write-Host "Portable executable: $exe"
    Write-Host "SHA-256: $($hash.Hash)"
    Write-Host "Portable archive: $archive"
    Write-Host "Archive SHA-256: $($archiveHash.Hash)"
}
finally {
    Pop-Location
}
