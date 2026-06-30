param(
    [switch] $RemoveSdk
)

$ErrorActionPreference = "Stop"

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$targets = @(
    "artifacts",
    "TestResults",
    "test-results",
    "web\dist",
    "web\node_modules",
    "web\test-results",
    "src\SteerCast.App\bin",
    "src\SteerCast.App\obj",
    "src\SteerCast.Core\bin",
    "src\SteerCast.Core\obj",
    "tests\SteerCast.Tests\bin",
    "tests\SteerCast.Tests\obj"
)

if ($RemoveSdk) {
    $targets += ".dotnet"
}

foreach ($target in $targets) {
    $path = [System.IO.Path]::GetFullPath((Join-Path $root $target))
    if (-not $path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside the repository: $path"
    }

    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
        Write-Host "Removed $target"
    }
}

Write-Host "Project cleanup complete."
