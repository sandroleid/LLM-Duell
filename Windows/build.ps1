# Baut die Windows-Version von LLM Duell.
#
#   .\Windows\build.ps1                 →  dist\LLM Duell.exe
#   .\Windows\build.ps1 -Package        →  zusätzlich dist\LLM-Duell-<Version>-Windows.zip
#                                          und dist\LLM-Duell-<Version>-Setup.exe
#   .\Windows\build.ps1 -Version 1.2.0  →  Versionsnummer setzen (Standard: aus LLMDuell.csproj)
#
# Benötigt das .NET SDK (https://dot.net/download), für -Package zusätzlich Inno Setup 6
# (https://jrsoftware.org/isdl.php oder „winget install JRSoftware.InnoSetup“).
param(
    [string]$Version,
    [switch]$Package
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $PSScriptRoot 'LLMDuell.csproj'
$dist = Join-Path $root 'dist'

if (-not $Version) {
    $Version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}

Write-Host "→ Kompiliere LLM Duell $Version"
$out = Join-Path $root 'build\windows'
dotnet build $project -c Release -o $out -p:Version=$Version --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Build fehlgeschlagen' }

New-Item -ItemType Directory -Force $dist | Out-Null
$exe = Join-Path $dist 'LLM Duell.exe'
Copy-Item (Join-Path $out 'LLMDuell.exe') $exe -Force
Write-Host "✓ App: $exe ($([math]::Round((Get-Item $exe).Length / 1KB)) KB)"

if ($Package) {
    $zip = Join-Path $dist "LLM-Duell-$Version-Windows.zip"
    Write-Host "→ Erstelle $zip"
    Compress-Archive -Path $exe -DestinationPath $zip -Force

    $iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source
    if (-not $iscc) {
        $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
                  "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) wurde nicht gefunden' }

    Write-Host '→ Erstelle Installer'
    & $iscc "/DVersion=$Version" /Qp (Join-Path $PSScriptRoot 'installer.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer fehlgeschlagen' }

    Get-ChildItem $dist -File | Where-Object { $_.Name -like "LLM-Duell-$Version-*" } | ForEach-Object {
        Write-Host "✓ $($_.Name)  SHA256 $((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower())"
    }
}
