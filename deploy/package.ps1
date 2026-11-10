# Builds the release zip: dist\UltraLightRgb-<version>-win-x64.zip with app\, install and uninstall
# scripts, example configuration, README and licence texts. Needs the .NET 8 SDK; no elevation.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[xml]$proj = Get-Content (Join-Path $root 'src\UltraLightRgb\UltraLightRgb.csproj')
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'Version not found in UltraLightRgb.csproj' }

$name = "UltraLightRgb-$version-win-x64"
$stage = Join-Path $root "dist\$name"
$zip = Join-Path $root "dist\$name.zip"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }

foreach ($p in 'src\UltraLightRgb\UltraLightRgb.csproj', 'src\UltraLightRgb.Switch\UltraLightRgb.Switch.csproj') {
    dotnet publish (Join-Path $root $p) -c Release -o (Join-Path $stage 'app') -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish $p failed with exit code $LASTEXITCODE" }
}
# debug symbols are not needed to run it and only grow the download
Get-ChildItem (Join-Path $stage 'app') -Filter *.pdb | Remove-Item

Copy-Item (Join-Path $root 'deploy\install.ps1'), (Join-Path $root 'deploy\uninstall.ps1') $stage
Copy-Item (Join-Path $root 'devices.example.json') (Join-Path $stage 'app')
Copy-Item (Join-Path $root 'README.md'), (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD-PARTY-NOTICES.md') $stage
Copy-Item (Join-Path $root 'licenses') $stage -Recurse

Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
"zip=$zip"
"bytes=$((Get-Item $zip).Length)"
"sha256=$((Get-FileHash $zip -Algorithm SHA256).Hash)"
