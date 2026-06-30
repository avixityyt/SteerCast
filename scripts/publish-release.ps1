param(
    [string] $Version,
    [string] $GitHubRepo = "avixityyt/SteerCast",
    [string] $ShareXPath,
    [string] $ShareXTask,
    [ValidateSet("Installer", "All")]
    [string] $ShareXFiles = "Installer",
    [switch] $SkipShareX,
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

function Find-ShareX {
    if (-not [string]::IsNullOrWhiteSpace($ShareXPath)) {
        if (-not (Test-Path -LiteralPath $ShareXPath)) {
            throw "ShareX was not found at: $ShareXPath"
        }
        return $ShareXPath
    }

    $command = Get-Command ShareX -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    @(
        (Join-Path $env:ProgramFiles "ShareX\ShareX.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "ShareX\ShareX.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\ShareX\ShareX.exe"),
        (Join-Path $env:LOCALAPPDATA "ShareX\ShareX.exe")
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
}

function Assert-CleanGitTree {
    $dirty = & git -C $root status --porcelain --untracked-files=no
    if ($dirty -and -not $AllowDirty) {
        throw "Tracked files are dirty. Commit/stash first, or pass -AllowDirty."
    }
}

function Publish-ToShareX([string] $shareX, [System.IO.FileInfo[]] $files, [string] $version) {
    $before = try { Get-Clipboard -Raw } catch { "" }
    foreach ($file in $files) {
        Write-Host "Uploading with ShareX: $($file.Name)"
        $arguments = @($file.FullName, "-autoclose")
        if (-not [string]::IsNullOrWhiteSpace($ShareXTask)) {
            $arguments += @("-task", $ShareXTask)
        }
        Start-Process -FilePath $shareX -ArgumentList $arguments -Wait
    }

    Start-Sleep -Milliseconds 500
    $after = try { Get-Clipboard -Raw } catch { "" }
    if ($after -and $after -ne $before) {
        $log = Join-Path $root "artifacts\sharex-upload-$version.txt"
        Set-Content -LiteralPath $log -Value $after -Encoding utf8
        Write-Host "ShareX clipboard result saved to $log"
    }
    else {
        Write-Warning "ShareX finished, but no new clipboard URL was detected."
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

if (-not $SkipShareX) {
    $shareX = Find-ShareX
    if ($shareX) {
        $shareXUploadFiles = if ($ShareXFiles -eq "All") { $releaseFiles } else { @($installer) }
        Publish-ToShareX -shareX $shareX -files $shareXUploadFiles -version $resolvedVersion
    }
    else {
        Write-Warning "ShareX was not found. Pass -ShareXPath or use -SkipShareX."
    }
}

if (-not $SkipGitHub) {
    Publish-ToGitHub -version $resolvedVersion -files $releaseFiles
}

Write-Host "Release publish flow complete for SteerCast $resolvedVersion."
