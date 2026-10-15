#Requires -Version 5.1
<#
.SYNOPSIS
    Remediation script for the Iru (formerly Kandji) Windows Custom Script Library Item.

.DESCRIPTION
    Paste this into the "Remediation Script" field of a Windows Custom Script Library Item (the audit
    script is Audit-MoveItMigration.ps1). Iru runs this, as SYSTEM, whenever the audit reports the
    device is not yet migrated. It:

      1. defers (does nothing) while an interactive user is logged on, so MoveIT never tries to
         migrate a loaded profile. Iru retries on the next check-in,
      2. downloads a package .zip (moveit.exe + Invoke-MoveItMigration.ps1 + moveit.json) that you
         host, verifies its hash, and extracts it,
      3. runs the standard wrapper, which performs the migration and writes the done marker on
         success. Once the marker exists, the audit passes and Iru stops running this.

    Exit codes (what Iru sees):
      0   migration succeeded (triggers the restart timer if you enabled "Restart after successful
          execution" on the Library Item; shows as Remediated)
      10  deferred because a user is logged on (shows as an alert; will retry next check-in)
      other non-zero = failure (shows as an alert; see the output and the MoveIT log)

    Let Iru own the reboot: enable "Restart after a successful execution" on the Library Item and keep
    "reboot": false in moveit.json, so users get the agent's restart countdown instead of an abrupt
    reboot from MoveIT.

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

# If $true, defer (exit 10) while any user is logged on. Leave $true unless you deliberately
# want to migrate while someone may be signed in (MoveIT will refuse a loaded profile anyway).
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
        Write-Host "Already migrated (marker present). Nothing to do."
        $exitCode = 0
    }
    elseif ($DeferIfUserLoggedOn -and (Test-AnyUserLoggedOn)) {
        Write-Host "DEFERRED: a user is logged on. Will retry on the next check-in."
        $exitCode = 10
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
    Write-Host "Iru remediation error: $($_.Exception.Message)"
    $exitCode = 1
}
finally {
    if ($work -and (Test-Path $work)) {
        Remove-Item -Path $work -Recurse -Force -ErrorAction SilentlyContinue
    }
    Stop-Transcript | Out-Null
}

exit $exitCode
