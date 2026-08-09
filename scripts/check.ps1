[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot

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
    Invoke-CheckedCommand dotnet restore NowPlayingOverlay.sln
    Invoke-CheckedCommand dotnet build NowPlayingOverlay.sln --no-restore
    Invoke-CheckedCommand dotnet test NowPlayingOverlay.sln --no-build

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
}
finally {
    Pop-Location
}
