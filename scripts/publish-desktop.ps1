$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts\publish"
dotnet publish (Join-Path $root "src\NIVEX.Desktop\NIVEX.Desktop.csproj") -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $out
if ($LASTEXITCODE -ne 0) { throw "publish failed" }
$target = Join-Path $env:USERPROFILE "Downloads\NIVEX.exe"
Copy-Item (Join-Path $out "NIVEX.Desktop.exe") $target -Force
Write-Host "Updated $target"
