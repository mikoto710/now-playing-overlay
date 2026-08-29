[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$webRoot = Join-Path $repositoryRoot "web"
$playwrightCode = Join-Path $PSScriptRoot "overlay-layout.playwright.js"
$playwrightPackage = "@playwright/cli@0.1.18"
$sessionName = "npolayout$([guid]::NewGuid().ToString('N'))"
$previewProcess = $null
$browserOpened = $false
$temporaryPrefix = "now-playing-overlay-layout-$([guid]::NewGuid().ToString('N'))"
$temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$playwrightWorkspace = Join-Path $temporaryRoot "$temporaryPrefix-playwright"
$previewOutput = Join-Path $temporaryRoot "$temporaryPrefix.out.log"
$previewError = Join-Path $temporaryRoot "$temporaryPrefix.err.log"
$playwrightLocationActive = $false

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

function Get-AvailableLoopbackPort {
    $listener = [System.Net.Sockets.TcpListener]::new(
        [System.Net.IPAddress]::Loopback,
        0
    )
    $listener.Start()
    try {
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

$node = (Get-Command node -ErrorAction Stop).Source
$npx = (Get-Command npx.cmd -ErrorAction Stop).Source
$vite = Join-Path $webRoot "node_modules\vite\bin\vite.js"
if (-not (Test-Path -LiteralPath $vite -PathType Leaf)) {
    throw "Frontend dependencies are missing. Run 'npm --prefix web install' once, then rerun the layout check."
}

$port = Get-AvailableLoopbackPort
$overlayUrl = "http://127.0.0.1:$port/NowPlaying.html"
$cliPrefix = @(
    "--yes"
    "--package"
    $playwrightPackage
    "playwright-cli"
    "-s=$sessionName"
)

New-Item -ItemType Directory -Path $playwrightWorkspace | Out-Null
try {
    Push-Location $webRoot
    try {
        Invoke-CheckedCommand npm run build
    }
    finally {
        Pop-Location
    }

    Push-Location $playwrightWorkspace
    $playwrightLocationActive = $true

    $previewProcess = Start-Process `
        -FilePath $node `
        -ArgumentList @($vite, "preview", "--host", "127.0.0.1", "--port", $port, "--strictPort") `
        -WorkingDirectory $webRoot `
        -WindowStyle Hidden `
        -RedirectStandardOutput $previewOutput `
        -RedirectStandardError $previewError `
        -PassThru

    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($previewProcess.HasExited) {
            throw "Vite preview exited before the layout check started."
        }

        try {
            $response = Invoke-WebRequest -Uri $overlayUrl -UseBasicParsing -TimeoutSec 1
            if ($response.StatusCode -eq 200) {
                break
            }
        }
        catch {
            Start-Sleep -Milliseconds 100
        }
    }

    if ([DateTime]::UtcNow -ge $deadline) {
        throw "Timed out waiting for Vite preview at $overlayUrl."
    }

    $bootstrapUrl = "about:blank#$([System.Uri]::EscapeDataString($overlayUrl))"
    Invoke-CheckedCommand $npx @cliPrefix open $bootstrapUrl
    $browserOpened = $true
    Invoke-CheckedCommand $npx @cliPrefix --raw run-code --filename $playwrightCode
}
finally {
    if ($browserOpened) {
        & $npx @cliPrefix close | Out-Host
    }

    if ($null -ne $previewProcess -and -not $previewProcess.HasExited) {
        Stop-Process -Id $previewProcess.Id -Force
        $previewProcess.WaitForExit()
    }

    if ($playwrightLocationActive) {
        Pop-Location
    }

    foreach ($temporaryLog in @($previewOutput, $previewError)) {
        if (Test-Path -LiteralPath $temporaryLog -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryLog -Force
        }
    }

    $resolvedPlaywrightWorkspace = [System.IO.Path]::GetFullPath($playwrightWorkspace)
    if (-not $resolvedPlaywrightWorkspace.StartsWith(
        $temporaryRoot,
        [System.StringComparison]::OrdinalIgnoreCase
    )) {
        throw "Refusing to clean unexpected Playwright workspace '$resolvedPlaywrightWorkspace'."
    }
    if (Test-Path -LiteralPath $resolvedPlaywrightWorkspace -PathType Container) {
        Remove-Item -LiteralPath $resolvedPlaywrightWorkspace -Recurse -Force
    }
}
