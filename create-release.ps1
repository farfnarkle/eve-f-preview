# Builds the Windows release zip and creates a GitHub release (requires: gh auth login).
# Usage: .\create-release.ps1
# Optional: .\create-release.ps1 -Tag v1.0.0 -TargetBranch main

param(
    [string]$Tag = "v1.0.0",
    [string]$TargetBranch = "main"
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
Set-Location $RepoRoot

$Version = $Tag.TrimStart("v")
$StagingDir = Join-Path $RepoRoot "release-staging\EVE-F-Preview-$Tag-Windows"
$ZipPath = Join-Path $RepoRoot "release-staging\Release-$Tag-Windows.zip"
$NotesFile = Join-Path $RepoRoot ".github\RELEASE_$Tag.md"

if (-not (Test-Path $NotesFile)) {
    throw "Release notes not found: $NotesFile"
}

# AssemblyVersion requires four parts; pad 1.0.0 -> 1.0.0.0
$AssemblyVersion = $Version
if (($Version -split '\.').Count -eq 3) {
    $AssemblyVersion = "$Version.0"
}

Write-Host "Publishing EVE-F-Preview $Version..."
if (Test-Path $StagingDir) { Remove-Item $StagingDir -Recurse -Force }
New-Item -ItemType Directory -Path $StagingDir | Out-Null

dotnet publish "src\Eve-F-Preview\Eve-F-Preview.csproj" -c Release -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Version `
    -p:InformationalVersion=$Version `
    -p:AssemblyVersion=$AssemblyVersion `
    -p:FileVersion=$Version `
    -o $StagingDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "Creating zip..."
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path (Join-Path $StagingDir "*") -DestinationPath $ZipPath -Force

Write-Host "Creating GitHub release $Tag (target: $TargetBranch)..."
gh release create $Tag --repo farfnarkle/eve-f-preview --target $TargetBranch --title "EVE-F-Preview $Version" --notes-file $NotesFile $ZipPath

Write-Host "Done: https://github.com/farfnarkle/eve-f-preview/releases/tag/$Tag"
