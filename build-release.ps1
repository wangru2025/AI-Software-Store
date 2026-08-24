$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$releaseDir = Join-Path $root "release"
$clientProject = Join-Path $root "src\AIShop.Client\AIShop.Client.csproj"
$updaterProject = Join-Path $root "src\AIShop.Updater\AIShop.Updater.csproj"
$appName = "AI$([char]0x8F6F)$([char]0x4EF6)$([char]0x5546)$([char]0x5E97)"

if (Test-Path -LiteralPath $releaseDir) {
    Remove-Item -LiteralPath $releaseDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

dotnet publish $clientProject -c Release -o $releaseDir
dotnet publish $updaterProject -c Release -o $releaseDir

$clientExe = Join-Path $releaseDir "$appName.exe"
$updaterExe = Join-Path $releaseDir "$appName.Updater.exe"

if (!(Test-Path -LiteralPath $clientExe)) {
    throw "Missing client output: $clientExe"
}

if (!(Test-Path -LiteralPath $updaterExe)) {
    throw "Missing updater output: $updaterExe"
}

Write-Host "Release build completed: $releaseDir"
