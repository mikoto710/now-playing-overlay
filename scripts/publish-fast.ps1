[CmdletBinding()]
param(
    [switch]$BuildWeb
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$hostProject = Join-Path $repositoryRoot "host\NowPlayingOverlay.Host.csproj"
$artifactsDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot "artifacts")
)
$publishDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $artifactsDirectory "publish\win-x64")
)
$expectedExecutable = Join-Path $publishDirectory "NowPlayingOverlay.exe"

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)]
        [string]$Executable,

        [Parameter(ValueFromRemainingArguments)]
        [string[]]$Arguments
    )

    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE`: $Executable $Arguments"
    }
}

if (-not $publishDirectory.StartsWith(
    "$artifactsDirectory$([System.IO.Path]::DirectorySeparatorChar)",
    [System.StringComparison]::OrdinalIgnoreCase
)) {
    throw "Refusing to use unexpected publish directory '$publishDirectory'."
}

if (-not (Test-Path -LiteralPath $hostProject -PathType Leaf)) {
    throw "Host project is missing at '$hostProject'."
}

Push-Location $repositoryRoot
try {
    if ($BuildWeb) {
        Invoke-CheckedCommand npm --prefix web run build
    }

    if (Test-Path -LiteralPath $publishDirectory) {
        Remove-Item -LiteralPath $publishDirectory -Recurse -Force
    }

    $publishArguments = @(
        "publish"
        $hostProject
        "--configuration"
        "Debug"
        "--runtime"
        "win-x64"
        "--self-contained"
        "false"
        "-p:NuGetAudit=false"
        "-p:PublishSingleFile=true"
        "-p:PublishTrimmed=false"
        "-p:DebugSymbols=false"
        "-p:DebugType=None"
        "--output"
        $publishDirectory
    )
    Invoke-CheckedCommand dotnet @publishArguments

    if (-not (Test-Path -LiteralPath $expectedExecutable -PathType Leaf)) {
        throw "Quick-published executable is missing at '$expectedExecutable'."
    }

    $publishedExecutable = Get-Item -LiteralPath $expectedExecutable
    if ($publishedExecutable.Length -le 0) {
        throw "Quick-published executable '$expectedExecutable' is empty."
    }

    $sizeMiB = [Math]::Round($publishedExecutable.Length / 1MB, 1)
    Write-Host "Fast-published $($publishedExecutable.FullName) ($sizeMiB MiB)"
    Write-Host "Tests and release packaging were skipped."
}
finally {
    Pop-Location
}
