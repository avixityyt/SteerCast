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

Push-Location (Join-Path $root "web")
try {
    Invoke-CheckedNative { npm ci } "npm ci"
    Invoke-CheckedNative { npm run typecheck } "Web typecheck"
    Invoke-CheckedNative { npm run build } "Web build"
    Invoke-CheckedNative { npm audit --audit-level=moderate } "npm audit"
}
finally {
    Pop-Location
}

Invoke-CheckedNative { & $dotnet test (Join-Path $root "tests\SteerCast.Tests\SteerCast.Tests.csproj") -c Release } ".NET tests"
Invoke-CheckedNative { & $dotnet build (Join-Path $root "SteerCast.sln") -c Release --no-restore } ".NET build"

Push-Location (Join-Path $root "web")
try {
    Invoke-CheckedNative { npm run test:browser } "Browser tests"
}
finally {
    Pop-Location
}
