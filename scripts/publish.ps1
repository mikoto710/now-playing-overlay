[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$checkScript = Join-Path $PSScriptRoot "check.ps1"
$hostProject = Join-Path $repositoryRoot "host\NowPlayingOverlay.Host.csproj"
$publishDirectory = Join-Path $repositoryRoot "artifacts\publish\win-x64"
$expectedExecutableName = "NowPlayingOverlay.exe"

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

if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
    throw "Publishing is supported only on Windows."
}

if (-not (Test-Path -LiteralPath $checkScript -PathType Leaf)) {
    throw "Validation script is missing at '$checkScript'."
}

if (-not (Test-Path -LiteralPath $hostProject -PathType Leaf)) {
    throw "Host project is missing at '$hostProject'."
}

Reset-PublishDirectory -Directory $publishDirectory

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

$sizeMiB = [Math]::Round($publishedExecutable.Length / 1MB, 1)
Write-Host "Published $($publishedExecutable.FullName) ($sizeMiB MiB)"
