[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$dotnetArtifactsRoot = [System.IO.Path]::GetFullPath(
    (Join-Path ([System.IO.Path]::GetTempPath()) "NowPlayingOverlay-validation-$([guid]::NewGuid().ToString('N'))")
)

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

Push-Location $repositoryRoot
try {
    Push-Location (Join-Path $repositoryRoot "web")
    try {
        Invoke-CheckedCommand npm ci
        Invoke-CheckedCommand npm run check
        Invoke-CheckedCommand npm test
        Invoke-CheckedCommand npm run build
    }
    finally {
        Pop-Location
    }

    # Keep validation isolated from IDE-owned bin/obj files so a live development
    # session cannot make the release gate fail through unrelated file locks.
    Invoke-CheckedCommand dotnet restore NowPlayingOverlay.sln --artifacts-path $dotnetArtifactsRoot
    Invoke-CheckedCommand dotnet build NowPlayingOverlay.sln --no-restore --artifacts-path $dotnetArtifactsRoot
    Invoke-CheckedCommand dotnet test NowPlayingOverlay.sln --no-build --artifacts-path $dotnetArtifactsRoot
}
finally {
    Pop-Location

    $systemTemporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if (-not $dotnetArtifactsRoot.StartsWith(
        $systemTemporaryRoot,
        [System.StringComparison]::OrdinalIgnoreCase
    )) {
        throw "Refusing to clean unexpected validation directory '$dotnetArtifactsRoot'."
    }

    if (Test-Path -LiteralPath $dotnetArtifactsRoot) {
        Remove-Item -LiteralPath $dotnetArtifactsRoot -Recurse -Force
    }
}
