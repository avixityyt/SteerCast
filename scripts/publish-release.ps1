param(
    [string] $Version,
    [string] $GitHubRepo = "avixityyt/SteerCast",
    [switch] $SkipGitHub,
    [switch] $Draft,
    [switch] $Prerelease,
    [switch] $AllowDirty
)

$ErrorActionPreference = "Stop"

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))

function Get-SteerCastVersion {
    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        return $Version
    }

    [xml] $project = Get-Content (Join-Path $root "src\SteerCast.App\SteerCast.App.csproj")
    $detected = [string] $project.Project.PropertyGroup.Version
    if ([string]::IsNullOrWhiteSpace($detected)) {
        throw "Could not determine version from SteerCast.App.csproj."
    }
    return $detected
}

function Assert-CleanGitTree {
    $dirty = & git -C $root status --porcelain --untracked-files=no
    if ($dirty -and -not $AllowDirty) {
        throw "Tracked files are dirty. Commit/stash first, or pass -AllowDirty."
    }
}

function Publish-ToGitHub([string] $version, [System.IO.FileInfo[]] $files) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "GitHub CLI was not found. Install gh or pass -SkipGitHub."
    }

    $tag = "v$version"
    & git -C $root push origin HEAD
    if (-not (& git -C $root tag --list $tag)) {
        & git -C $root tag -a $tag -m "SteerCast $version"
    }
    & git -C $root push origin $tag

    $releaseExists = $true
    & gh release view $tag --repo $GitHubRepo *> $null
    if ($LASTEXITCODE -ne 0) {
        $releaseExists = $false
    }

    if ($releaseExists) {
        & gh release upload $tag @($files.FullName) --repo $GitHubRepo --clobber
    }
    else {
        $arguments = @(
            "release", "create", $tag,
            "--repo", $GitHubRepo,
            "--title", "SteerCast $version",
            "--notes", "SteerCast $version"
        )
        if ($Draft) { $arguments += "--draft" }
        if ($Prerelease) { $arguments += "--prerelease" }
        $arguments += $files.FullName
        & gh @arguments
    }
}

$resolvedVersion = Get-SteerCastVersion
Assert-CleanGitTree

& (Join-Path $PSScriptRoot "package.ps1") -Version $resolvedVersion

$artifacts = Join-Path $root "artifacts"
$installer = Get-Item (Join-Path $artifacts "SteerCast-$resolvedVersion-Setup.exe")
$portable = Get-Item (Join-Path $artifacts "SteerCast-$resolvedVersion-win-x64.zip")
$releaseFiles = @($installer, $portable)

if (-not $SkipGitHub) {
    Publish-ToGitHub -version $resolvedVersion -files $releaseFiles
}

Write-Host "Release publish flow complete for SteerCast $resolvedVersion."
