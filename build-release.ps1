<#
.SYNOPSIS
    Builds the release zips of Comic Archive Converter.

.DESCRIPTION
    1. Runs the automated tests.
    2. Publishes two self-contained builds (the .exe plus every DLL it needs, including the
       .NET runtime), so they run without installing anything:

         ComicArchiveConverter-v<version>-windows.zip   32-bit build: runs on EVERY Windows 10/11 PC
                                                         (32-bit, 64-bit and ARM)
         ComicArchiveConverter-v<version>-win-x64.zip   64-bit build for 64-bit Windows 10/11

    3. Adds a short README, the licence and the third-party notices to each.

    Each zip contains ONE folder, so it unpacks tidily:

        ComicArchiveConverter\
            ComicArchiveConverter.exe      <- double-click this
            README.txt
            LICENSE.txt
            THIRD-PARTY-NOTICES.txt
            *.dll                          <- needed by the .exe, keep them next to it

    Used by build-release.bat on your PC and by the GitHub Actions workflow.

.PARAMETER SkipTests
    Build without running the tests first.
#>
param([switch]$SkipTests)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# Zips a folder (including the folder itself) with standard "/" separators inside the zip.
# Windows PowerShell's Compress-Archive writes "\" instead, which some unzip tools mishandle.
function New-ReleaseZip([string]$sourceFolder, [string]$zipPath) {
    $base = Split-Path $sourceFolder -Parent
    $stream = [System.IO.File]::Open($zipPath, [System.IO.FileMode]::Create)
    $archive = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        Get-ChildItem $sourceFolder -Recurse -File | ForEach-Object {
            $entryName = $_.FullName.Substring($base.Length + 1).Replace('\', '/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, $_.FullName, $entryName, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally {
        $archive.Dispose()
        $stream.Dispose()
    }
}

# The version number lives in one place: Directory.Build.props
$version = (Select-Xml -Path 'Directory.Build.props' -XPath '//Version').Node.InnerText
$name = 'ComicArchiveConverter'
$publishRoot = Join-Path $PSScriptRoot 'publish'

# Which builds to make. x86 is listed first because it is the one that works everywhere.
$targets = @(
    @{ Runtime = 'win-x86'; Suffix = 'windows'; Note = 'Works on every Windows 10/11 PC: 32-bit, 64-bit and ARM.' },
    @{ Runtime = 'win-x64'; Suffix = 'win-x64'; Note = 'For 64-bit Windows 10/11 PCs.' }
)

Write-Host ""
Write-Host "=== Comic Archive Converter $version ===" -ForegroundColor Cyan

if (-not $SkipTests) {
    Write-Host "`n--- Running tests ---" -ForegroundColor Cyan
    dotnet test 'tests\ComicArchiveConverter.Tests\ComicArchiveConverter.Tests.csproj' -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed. Nothing was published.' }
}

if (Test-Path (Join-Path $publishRoot 'staging')) { Remove-Item (Join-Path $publishRoot 'staging') -Recurse -Force }

foreach ($target in $targets) {
    $suffix = $target.Suffix
    $staging = Join-Path $publishRoot "staging\$suffix\$name"
    $zip = Join-Path $publishRoot "$name-v$version-$suffix.zip"

    Write-Host "`n--- Publishing $($target.Runtime) ---" -ForegroundColor Cyan
    # The profile holds the shared settings; the runtime and output folder are chosen here.
    # DebugType=none keeps .pdb debug files out of the download.
    dotnet publish 'src\ComicArchiveConverter\ComicArchiveConverter.csproj' -p:PublishProfile=windows `
        "-p:RuntimeIdentifier=$($target.Runtime)" -p:DebugType=none "-p:PublishDir=$staging\"
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $($target.Runtime)." }

    # README with the version and which build this is filled in, plus the licence and notices.
    (Get-Content 'docs\RELEASE-README.txt' -Raw).Replace('{VERSION}', $version).Replace('{BUILD}', $target.Note) |
        Set-Content (Join-Path $staging 'README.txt') -Encoding UTF8
    Copy-Item 'LICENSE' (Join-Path $staging 'LICENSE.txt')
    Copy-Item 'THIRD-PARTY-NOTICES.md' (Join-Path $staging 'THIRD-PARTY-NOTICES.txt')

    if (Test-Path $zip) { Remove-Item $zip -Force }
    New-ReleaseZip $staging $zip

    $sizeMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
    Write-Host "  $zip  ($sizeMb MB)" -ForegroundColor Green
}

Write-Host "`nDone! Both zips are in: $publishRoot" -ForegroundColor Green
