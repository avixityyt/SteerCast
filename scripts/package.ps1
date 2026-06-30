param(
    [string] $Version
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) {
    $dotnet = "dotnet"
}

function Invoke-CheckedNative {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock] $Command,
        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml] $project = Get-Content (Join-Path $root "src\SteerCast.App\SteerCast.App.csproj")
    $Version = [string] $project.Project.PropertyGroup.Version
}
if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Could not determine the SteerCast version."
}

& (Join-Path $PSScriptRoot "build.ps1")

$artifacts = Join-Path $root "artifacts"
$publish = Join-Path $artifacts "publish"
$portable = Join-Path $artifacts "SteerCast-$Version-win-x64.zip"
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
if (Test-Path $publish) {
    $expected = [System.IO.Path]::GetFullPath((Join-Path $artifacts "publish"))
    $actual = (Resolve-Path $publish).Path
    if ($actual -ne $expected) {
        throw "Refusing to clear unexpected publish path: $actual"
    }
    Remove-Item -LiteralPath $actual -Recurse -Force
}

Invoke-CheckedNative {
    & $dotnet publish (Join-Path $root "src\SteerCast.App\SteerCast.App.csproj") `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -o $publish `
        --no-restore
} ".NET publish"

Get-ChildItem -LiteralPath $publish -Filter *.pdb -File | Remove-Item -Force
Copy-Item (Join-Path $root "LICENSE") $publish -Force
Copy-Item (Join-Path $root "THIRD_PARTY_NOTICES.md") $publish -Force
Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $portable -Force

$iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
$isccPath = if ($iscc) {
    $iscc.Source
}
else {
    @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe")
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if ($isccPath) {
    Invoke-CheckedNative { & $isccPath (Join-Path $root "installer\SteerCast.iss") "/DMyAppVersion=$Version" } "Inno Setup compile"
}
else {
    Write-Host "Inno Setup was not found; portable ZIP created without an installer."
}

Write-Host "Packages are available under $artifacts"
