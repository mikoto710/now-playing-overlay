[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$checkScript = Join-Path $PSScriptRoot "check.ps1"
$hostProject = Join-Path $repositoryRoot "host\NowPlayingOverlay.Host.csproj"
$publishDirectory = Join-Path $repositoryRoot "artifacts\publish\win-x64"
$releaseDirectory = Join-Path $repositoryRoot "artifacts\release"
$releaseVersion = "0.3.0"
$expectedExecutableName = "NowPlayingOverlay.exe"
$releaseArchiveName = "NowPlayingOverlay-v$releaseVersion-win-x64.zip"
$publishArtifactsRoot = [System.IO.Path]::GetFullPath(
    (Join-Path ([System.IO.Path]::GetTempPath()) "NowPlayingOverlay-publish-$([guid]::NewGuid().ToString('N'))")
)

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

    $browserProducerPath = Join-Path $repositoryRoot "integrations\NowPlayingOverlay.user.js"
    $browserProducer = Get-Content -LiteralPath $browserProducerPath -Raw
    $versionMatch = [regex]::Match(
        $browserProducer,
        "(?m)^// @version\s+(?<version>\S+)\s*$"
    )
    if (-not $versionMatch.Success -or $versionMatch.Groups["version"].Value -ne $Version) {
        throw "Expected browser Producer version '$Version', but found '$($versionMatch.Groups["version"].Value)'."
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

function Assert-DesktopRuntimeClosure {
    $temporaryRoot = [System.IO.Path]::GetFullPath(
        (Join-Path ([System.IO.Path]::GetTempPath()) "NowPlayingOverlay-runtime-closure-$([guid]::NewGuid().ToString('N'))")
    )
    $publishRoot = Join-Path $temporaryRoot "publish"
    try {
        $closureArguments = @(
            "publish"
            $hostProject
            "--configuration"
            "Release"
            "--runtime"
            "win-x64"
            "--self-contained"
            "false"
            "-p:PublishSingleFile=false"
            "-p:UseAppHost=false"
            "-p:DebugSymbols=false"
            "-p:DebugType=None"
            "--artifacts-path"
            $temporaryRoot
            "--output"
            $publishRoot
        )
        Invoke-CheckedCommand -Executable "dotnet" -Arguments $closureArguments

        $runtimeConfigPath = Join-Path $publishRoot "NowPlayingOverlay.runtimeconfig.json"
        $dependencyManifestPath = Join-Path $publishRoot "NowPlayingOverlay.deps.json"
        foreach ($requiredPath in @($runtimeConfigPath, $dependencyManifestPath)) {
            if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
                throw "Runtime closure manifest is missing at '$requiredPath'."
            }
        }

        $runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
        $frameworkNames = @($runtimeConfig.runtimeOptions.frameworks | ForEach-Object name | Sort-Object)
        $expectedFrameworkNames = @("Microsoft.NETCore.App", "Microsoft.WindowsDesktop.App")
        if (($frameworkNames -join "`n") -cne ($expectedFrameworkNames -join "`n")) {
            throw "Unexpected framework-dependent runtime closure: $($frameworkNames -join ', ')."
        }

        $dependencyManifest = Get-Content -LiteralPath $dependencyManifestPath -Raw
        $forbiddenDependencies = @(
            "Microsoft.AspNetCore.App"
            "Microsoft.AspNetCore."
            "Microsoft.Web.WebView2"
            "Microsoft.Windows.SDK.NET"
            "WinRT.Runtime"
        )
        foreach ($forbiddenDependency in $forbiddenDependencies) {
            if ($dependencyManifest.IndexOf(
                $forbiddenDependency,
                [System.StringComparison]::OrdinalIgnoreCase
            ) -ge 0) {
                throw "Forbidden runtime dependency '$forbiddenDependency' entered the publish closure."
            }
        }

        $requiredApplicationLibraries = @(
            "Microsoft.Extensions.DependencyInjection.Abstractions.dll"
            "Microsoft.Extensions.Logging.Abstractions.dll"
            "NowPlayingOverlay.dll"
            "NowPlayingOverlay.WinRT.dll"
        )
        foreach ($requiredLibrary in $requiredApplicationLibraries) {
            $requiredPath = Join-Path $publishRoot $requiredLibrary
            if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
                throw "Expected application-owned runtime library '$requiredLibrary' is missing."
            }
        }

        Write-Host "Verified Desktop Runtime-only framework closure."
    }
    finally {
        $systemTemporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
        if (-not $temporaryRoot.StartsWith(
            $systemTemporaryRoot,
            [System.StringComparison]::OrdinalIgnoreCase
        )) {
            throw "Refusing to clean unexpected runtime closure directory '$temporaryRoot'."
        }

        if (Test-Path -LiteralPath $temporaryRoot) {
            Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
        }
    }
}

function New-ReleasePackage {
    param(
        [Parameter(Mandatory)]
        [System.IO.FileInfo]$Executable,

        [Parameter(Mandatory)]
        [string]$Directory,

        [Parameter(Mandatory)]
        [string]$ArchiveName
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

    $releaseItems = @(Get-ChildItem -LiteralPath $Directory -Force)
    $expectedReleaseItems = @($ArchiveName)
    $actualReleaseItems = @($releaseItems.Name | Sort-Object)
    if (($actualReleaseItems -join "`n") -cne (($expectedReleaseItems | Sort-Object) -join "`n")) {
        throw "Unexpected release directory entries: $($actualReleaseItems -join ', ')"
    }

    return [pscustomobject]@{
        Archive = Get-Item -LiteralPath $archivePath
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

& $checkScript -Release
if (-not $?) {
    throw "Repository validation failed."
}

Assert-DesktopRuntimeClosure

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
    "--artifacts-path"
    $publishArtifactsRoot
    "--output"
    $publishDirectory
)

try {
    Invoke-CheckedCommand -Executable "dotnet" -Arguments $publishArguments
    $publishedExecutable = Assert-SingleExecutableOutput `
        -Directory $publishDirectory `
        -ExecutableName $expectedExecutableName
    Assert-ExecutableIdentity -Executable $publishedExecutable -Version $releaseVersion

    $releasePackage = New-ReleasePackage `
        -Executable $publishedExecutable `
        -Directory $releaseDirectory `
        -ArchiveName $releaseArchiveName

    $sizeMiB = [Math]::Round($publishedExecutable.Length / 1MB, 1)
    Write-Host "Published $($publishedExecutable.FullName) ($sizeMiB MiB)"
    Write-Host "Packaged $($releasePackage.Archive.FullName)"
    Write-Host "SHA-256 $($releasePackage.Sha256)"
}
finally {
    $systemTemporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if (-not $publishArtifactsRoot.StartsWith(
        $systemTemporaryRoot,
        [System.StringComparison]::OrdinalIgnoreCase
    )) {
        throw "Refusing to clean unexpected publish artifacts directory '$publishArtifactsRoot'."
    }

    if (Test-Path -LiteralPath $publishArtifactsRoot) {
        Remove-Item -LiteralPath $publishArtifactsRoot -Recurse -Force
    }
}
