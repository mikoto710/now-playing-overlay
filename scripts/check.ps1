[CmdletBinding()]
param(
    [switch]$Release
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$dotnetArtifactsRoot = if ($Release) {
    [System.IO.Path]::GetFullPath(
        (Join-Path ([System.IO.Path]::GetTempPath()) "NowPlayingOverlay-validation-$([guid]::NewGuid().ToString('N'))")
    )
}
else {
    [System.IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot "artifacts\validation\dotnet")
    )
}

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
        if ($Release) {
            Invoke-CheckedCommand npm ci
        }
        elseif (-not (Test-Path -LiteralPath "node_modules" -PathType Container)) {
            throw "Frontend dependencies are missing. Run 'npm --prefix web install' once, then rerun the check."
        }

        Invoke-CheckedCommand npm run check
        Invoke-CheckedCommand npm test
        Invoke-CheckedCommand npm run build
    }
    finally {
        Pop-Location
    }

    Invoke-CheckedCommand node --test "integrations\tests\browser-producer.test.js"

    if ($Release) {
        & (Join-Path $PSScriptRoot "check-overlay-layout.ps1")
    }

    $testArguments = @(
        "test"
        "host.tests\NowPlayingOverlay.Host.Tests.csproj"
        "--artifacts-path"
        $dotnetArtifactsRoot
        "--disable-build-servers"
        "-m:2"
        "-nodeReuse:false"
        "-p:UseSharedCompilation=false"
    )
    Invoke-CheckedCommand dotnet @testArguments

    if ($Release) {
        $probeTestArguments = @(
            "test"
            "tools\session-probe.tests\NowPlayingOverlay.SessionProbe.Tests.csproj"
            "--artifacts-path"
            $dotnetArtifactsRoot
            "--disable-build-servers"
            "-m:2"
            "-nodeReuse:false"
            "-p:UseSharedCompilation=false"
        )
        Invoke-CheckedCommand dotnet @probeTestArguments
    }
}
finally {
    Pop-Location

    if ($Release) {
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
}
