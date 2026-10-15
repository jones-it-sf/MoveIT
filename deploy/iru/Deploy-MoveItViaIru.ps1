#Requires -Version 5.1
<#
.SYNOPSIS
    Runs a MoveIT migration from Iru (formerly Kandji) on Windows, via the Iru Agent's
    custom PowerShell script channel.

.DESCRIPTION
    Paste this into an Iru "custom script" Library Item assigned to a Windows Blueprint. The Iru
    Agent runs it in the agent's context (SYSTEM). It:

      1. exits early if the migration already ran on this device (the done marker),
      2. optionally defers while an interactive user is still logged on (MoveIT cannot migrate a
         profile that is currently loaded),
      3. downloads a package .zip (moveit.exe + Invoke-MoveItMigration.ps1 + moveit.json) that you
         host, verifies its hash, and extracts it,
      4. runs the standard wrapper (Invoke-MoveItMigration.ps1) so exit-code mapping and the
         run-once marker behave exactly like every other deployment method.

    Exit codes (what Iru sees): 0 = success or deferred or already done; non-zero = failure.

    Edit the CONFIG block below. Nothing else needs changing.
#>

# ----------------------------- CONFIG (edit these) -----------------------------

# URL to a .zip that contains moveit.exe, Invoke-MoveItMigration.ps1, and your moveit.json.
# Host it anywhere the device can reach over HTTPS (Azure Blob + SAS, S3, a GitHub release
# asset, or an internal web server). Build the exe with:
#   dotnet publish src/MoveIT.Cli -c Release -r win-x64   (win-arm64 for ARM devices)
$PackageUrl = 'https://example.com/moveit/moveit-package.zip'

# Optional SHA-256 of the .zip. Strongly recommended. Leave '' to skip verification.
# Get it with:  (Get-FileHash .\moveit-package.zip -Algorithm SHA256).Hash
$PackageSha256 = ''

# If $true, skip (and retry on the next agent check-in) when any user is logged on.
# Leave this $true unless you run during a maintenance window with no one signed in.
$DeferIfUserLoggedOn = $true

# Extra arguments to moveit.exe instead of using moveit.json. Leave @() to use the config.
# Example: @('migrate','--all','--generate-passwords','--leave-entra')
$ExtraArgs = @()

# ------------------------- end CONFIG (do not edit below) ----------------------

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$dataDir = Join-Path $env:ProgramData 'MoveIT'
$logDir = Join-Path $dataDir 'Logs'
$markerPath = Join-Path $dataDir 'migration.done'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$transcript = Join-Path $logDir ("iru-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))
Start-Transcript -Path $transcript -Append | Out-Null

function Test-AnyUserLoggedOn {
    # explorer.exe runs once per interactive desktop session; its presence means someone is on.
    try {
        $sessions = Get-CimInstance Win32_Process -Filter "Name='explorer.exe'" -ErrorAction Stop
        return [bool]$sessions
    }
    catch {
        return $false
    }
}

$exitCode = 1
$work = $null
try {
    if (Test-Path $markerPath) {
        Write-Host "Already migrated on this device (marker $markerPath present). Nothing to do."
        $exitCode = 0
    }
    elseif ($DeferIfUserLoggedOn -and (Test-AnyUserLoggedOn)) {
        Write-Host "A user is logged on; deferring until no one is signed in. Will retry next check-in."
        $exitCode = 0
    }
    else {
        if ([string]::IsNullOrWhiteSpace($PackageUrl) -or $PackageUrl -like '*example.com*') {
            throw "Set `$PackageUrl to the URL of your MoveIT package .zip."
        }

        $work = Join-Path $env:TEMP ("moveit-iru-{0}" -f ([guid]::NewGuid().ToString('N')))
        New-Item -ItemType Directory -Force -Path $work | Out-Null
        $zip = Join-Path $work 'moveit-package.zip'

        Write-Host "Downloading package from $PackageUrl"
        Invoke-WebRequest -Uri $PackageUrl -OutFile $zip -UseBasicParsing

        if (-not [string]::IsNullOrWhiteSpace($PackageSha256)) {
            $actual = (Get-FileHash -Path $zip -Algorithm SHA256).Hash
            if ($actual -ne $PackageSha256.Trim()) {
                throw "Package hash mismatch. Expected $PackageSha256 but got $actual."
            }
            Write-Host "Package hash verified."
        }

        $payload = Join-Path $work 'payload'
        Expand-Archive -Path $zip -DestinationPath $payload -Force

        $wrapper = Join-Path $payload 'Invoke-MoveItMigration.ps1'
        if (-not (Test-Path $wrapper)) {
            throw "Invoke-MoveItMigration.ps1 not found in the package."
        }

        $wrapperArgs = @{ RunOnce = $true; MarkerPath = $markerPath }
        if ($ExtraArgs.Count -gt 0) {
            $wrapperArgs['ArgumentList'] = $ExtraArgs
        }
        else {
            $wrapperArgs['ConfigPath'] = (Join-Path $payload 'moveit.json')
        }

        Write-Host "Invoking wrapper..."
        & $wrapper @wrapperArgs
        $exitCode = $LASTEXITCODE
        Write-Host "Wrapper exit code: $exitCode"
    }
}
catch {
    Write-Host "Iru bootstrapper error: $($_.Exception.Message)"
    $exitCode = 1
}
finally {
    if ($work -and (Test-Path $work)) {
        Remove-Item -Path $work -Recurse -Force -ErrorAction SilentlyContinue
    }
    Stop-Transcript | Out-Null
}

exit $exitCode
