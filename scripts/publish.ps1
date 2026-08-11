[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$checkScript = Join-Path $PSScriptRoot "check.ps1"
$hostProject = Join-Path $repositoryRoot "host\NowPlayingOverlay.Host.csproj"
$publishDirectory = Join-Path $repositoryRoot "artifacts\publish\win-x64"
$releaseDirectory = Join-Path $repositoryRoot "artifacts\release"
$releaseVersion = "0.1.0"
$expectedExecutableName = "NowPlayingOverlay.exe"
$releaseArchiveName = "NowPlayingOverlay-v$releaseVersion-win-x64.zip"
$releaseChecksumName = "$releaseArchiveName.sha256"

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)]
        [string]$Executable,

        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE`: $Executable $Arguments"
    }
}

function Assert-ReleaseVersionSources {
    param(
        [Parameter(Mandatory)]
        [string]$Version
    )

    [xml]$hostProjectXml = Get-Content -LiteralPath $hostProject -Raw
    $hostVersionNode = $hostProjectXml.SelectSingleNode("/Project/PropertyGroup/Version")
    $hostVersion = if ($null -eq $hostVersionNode) { $null } else { $hostVersionNode.InnerText }
    if ($hostVersion -ne $Version) {
        throw "Expected host version '$Version', but found '$hostVersion'."
    }

    $webPackagePath = Join-Path $repositoryRoot "web\package.json"
    $webPackage = Get-Content -LiteralPath $webPackagePath -Raw | ConvertFrom-Json
    if ($webPackage.version -ne $Version) {
        throw "Expected web version '$Version', but found '$($webPackage.version)'."
    }
}

function Reset-PublishDirectory {
    param(
        [Parameter(Mandatory)]
        [string]$Directory
    )

    $expectedDirectory = [System.IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot "artifacts\publish\win-x64")
    )
    $actualDirectory = [System.IO.Path]::GetFullPath($Directory)

    # Keep recursive cleanup pinned to the repository-owned release directory.
    if (-not [string]::Equals(
        $actualDirectory,
        $expectedDirectory,
        [System.StringComparison]::OrdinalIgnoreCase
    )) {
        throw "Refusing to clean unexpected publish directory '$actualDirectory'."
    }

    if (Test-Path -LiteralPath $actualDirectory) {
        $existingItem = Get-Item -LiteralPath $actualDirectory -Force
        if (-not $existingItem.PSIsContainer) {
            throw "Publish path '$actualDirectory' exists but is not a directory."
        }

        Remove-Item -LiteralPath $actualDirectory -Recurse -Force
    }

    $null = New-Item -ItemType Directory -Path $actualDirectory -Force
}

function Reset-ReleaseDirectory {
    param(
        [Parameter(Mandatory)]
        [string]$Directory
    )

    $expectedDirectory = [System.IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot "artifacts\release")
    )
    $actualDirectory = [System.IO.Path]::GetFullPath($Directory)

    if (-not [string]::Equals(
        $actualDirectory,
        $expectedDirectory,
        [System.StringComparison]::OrdinalIgnoreCase
    )) {
        throw "Refusing to clean unexpected release directory '$actualDirectory'."
    }

    if (Test-Path -LiteralPath $actualDirectory) {
        $existingItem = Get-Item -LiteralPath $actualDirectory -Force
        if (-not $existingItem.PSIsContainer) {
            throw "Release path '$actualDirectory' exists but is not a directory."
        }

        Remove-Item -LiteralPath $actualDirectory -Recurse -Force
    }

    $null = New-Item -ItemType Directory -Path $actualDirectory -Force
}

function Assert-SingleExecutableOutput {
    param(
        [Parameter(Mandatory)]
        [string]$Directory,

        [Parameter(Mandatory)]
        [string]$ExecutableName
    )

    $publishedItems = @(Get-ChildItem -LiteralPath $Directory -Force)
    if ($publishedItems.Count -ne 1) {
        $publishedNames = $publishedItems.Name -join ", "
        throw "Expected only '$ExecutableName' in '$Directory', but found $($publishedItems.Count) item(s): $publishedNames"
    }

    $publishedExecutable = $publishedItems[0]
    if ($publishedExecutable.PSIsContainer -or $publishedExecutable.Name -cne $ExecutableName) {
        throw "Expected only '$ExecutableName' in '$Directory', but found '$($publishedExecutable.Name)'."
    }

    if ($publishedExecutable.Length -le 0) {
        throw "Published executable '$($publishedExecutable.FullName)' is empty."
    }

    return $publishedExecutable
}

function Assert-ExecutableIdentity {
    param(
        [Parameter(Mandatory)]
        [System.IO.FileInfo]$Executable,

        [Parameter(Mandatory)]
        [string]$Version
    )

    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Executable.FullName)
    $expectedFileVersion = "$Version.0"
    if ($versionInfo.FileVersion -ne $expectedFileVersion) {
        throw "Expected file version '$expectedFileVersion', but found '$($versionInfo.FileVersion)'."
    }

    if ($versionInfo.ProductVersion -ne $Version) {
        throw "Expected product version '$Version', but found '$($versionInfo.ProductVersion)'."
    }

    if ($versionInfo.ProductName -ne "Now Playing Overlay") {
        throw "Expected product name 'Now Playing Overlay', but found '$($versionInfo.ProductName)'."
    }
}

function New-ReleasePackage {
    param(
        [Parameter(Mandatory)]
        [System.IO.FileInfo]$Executable,

        [Parameter(Mandatory)]
        [string]$Directory,

        [Parameter(Mandatory)]
        [string]$ArchiveName,

        [Parameter(Mandatory)]
        [string]$ChecksumName
    )

    $readmePath = Join-Path $repositoryRoot "README.md"
    $licensePath = Join-Path $repositoryRoot "LICENSE"
    foreach ($requiredPath in @($readmePath, $licensePath)) {
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "Release package input is missing at '$requiredPath'."
        }
    }

    $stagingDirectory = Join-Path $Directory "package"
    $null = New-Item -ItemType Directory -Path $stagingDirectory
    Copy-Item -LiteralPath $Executable.FullName -Destination $stagingDirectory
    Copy-Item -LiteralPath $readmePath -Destination $stagingDirectory
    Copy-Item -LiteralPath $licensePath -Destination $stagingDirectory

    $archivePath = Join-Path $Directory $ArchiveName
    $packageFiles = @(Get-ChildItem -LiteralPath $stagingDirectory -File)
    Compress-Archive -LiteralPath $packageFiles.FullName -DestinationPath $archivePath
    Remove-Item -LiteralPath $stagingDirectory -Recurse -Force

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $actualEntries = @($archive.Entries | ForEach-Object FullName | Sort-Object)
        $expectedEntries = @("LICENSE", "NowPlayingOverlay.exe", "README.md")
        if (($actualEntries -join "`n") -cne ($expectedEntries -join "`n")) {
            throw "Unexpected release archive entries: $($actualEntries -join ', ')"
        }

        foreach ($entry in $archive.Entries) {
            if ($entry.Length -le 0) {
                throw "Release archive entry '$($entry.FullName)' is empty."
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumPath = Join-Path $Directory $ChecksumName
    Set-Content -LiteralPath $checksumPath -Value "$archiveHash  $ArchiveName" -Encoding ascii -NoNewline

    $releaseItems = @(Get-ChildItem -LiteralPath $Directory -Force)
    $expectedReleaseItems = @($ArchiveName, $ChecksumName)
    $actualReleaseItems = @($releaseItems.Name | Sort-Object)
    if (($actualReleaseItems -join "`n") -cne (($expectedReleaseItems | Sort-Object) -join "`n")) {
        throw "Unexpected release directory entries: $($actualReleaseItems -join ', ')"
    }

    return [pscustomobject]@{
        Archive = Get-Item -LiteralPath $archivePath
        Checksum = Get-Item -LiteralPath $checksumPath
        Sha256 = $archiveHash
    }
}

if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
    throw "Publishing is supported only on Windows."
}

if (-not (Test-Path -LiteralPath $checkScript -PathType Leaf)) {
    throw "Validation script is missing at '$checkScript'."
}

if (-not (Test-Path -LiteralPath $hostProject -PathType Leaf)) {
    throw "Host project is missing at '$hostProject'."
}

Assert-ReleaseVersionSources -Version $releaseVersion
Reset-PublishDirectory -Directory $publishDirectory
Reset-ReleaseDirectory -Directory $releaseDirectory

& $checkScript
if (-not $?) {
    throw "Repository validation failed."
}

$publishArguments = @(
    "publish"
    $hostProject
    "--configuration"
    "Release"
    "--runtime"
    "win-x64"
    "--self-contained"
    "false"
    "-p:PublishSingleFile=true"
    "-p:PublishTrimmed=false"
    "-p:DebugSymbols=false"
    "-p:DebugType=None"
    "-p:StaticWebAssetsEnabled=false"
    "-p:IsTransformWebConfigDisabled=true"
    "--output"
    $publishDirectory
)

Invoke-CheckedCommand -Executable "dotnet" -Arguments $publishArguments
$publishedExecutable = Assert-SingleExecutableOutput `
    -Directory $publishDirectory `
    -ExecutableName $expectedExecutableName
Assert-ExecutableIdentity -Executable $publishedExecutable -Version $releaseVersion

$releasePackage = New-ReleasePackage `
    -Executable $publishedExecutable `
    -Directory $releaseDirectory `
    -ArchiveName $releaseArchiveName `
    -ChecksumName $releaseChecksumName

$sizeMiB = [Math]::Round($publishedExecutable.Length / 1MB, 1)
Write-Host "Published $($publishedExecutable.FullName) ($sizeMiB MiB)"
Write-Host "Packaged $($releasePackage.Archive.FullName)"
Write-Host "SHA-256 $($releasePackage.Sha256)"
