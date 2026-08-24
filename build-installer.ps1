$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$clientProject = Join-Path $root "src\AIShop.Client\AIShop.Client.csproj"
$iss = Get-ChildItem -LiteralPath (Join-Path $root "installer") -Filter "*.iss" | Select-Object -First 1
$outputDir = Join-Path $root "artifacts\installer"

if ($null -eq $iss) {
    throw "Inno Setup script was not found."
}

[xml]$project = Get-Content -LiteralPath $clientProject
$version = $project.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Client version is missing in $clientProject"
}

$candidate = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if (Test-Path -LiteralPath $candidate) {
    $isccPath = $candidate
}
else {
    $iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $iscc) {
        $isccPath = $iscc.Source
    }
}

if ([string]::IsNullOrWhiteSpace($isccPath)) {
    throw "Inno Setup compiler ISCC.exe was not found."
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

powershell -ExecutionPolicy Bypass -File (Join-Path $root "build-release.ps1")
& $isccPath "/DMyAppVersion=$version" $iss.FullName

$installer = Get-ChildItem -LiteralPath $outputDir -Filter "*-$version-setup.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($null -eq $installer) {
    throw "Missing installer output in $outputDir"
}

Write-Host "Installer build completed: $($installer.FullName)"
