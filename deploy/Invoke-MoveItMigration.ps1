#Requires -Version 5.1
<#
.SYNOPSIS
    Runs a MoveIT migration from a deployment tool (Intune, an RMM, or a GPO startup script).

.DESCRIPTION
    This is the single wrapper used by every deployment method. It is meant to run as SYSTEM.
    It locates moveit.exe and a config file next to itself, runs the migration non-interactively,
    writes a "done" marker on success, and maps MoveIT's exit codes to codes a deployment tool
    understands.

    MoveIT exit codes:
      0 success   1 failed   2 succeeded with warnings   3 bad arguments/config
      4 pre-flight failed or nothing to do (nothing was changed)

    This wrapper returns:
      0     success (MoveIT 0 or 2)
      3010  success, and -ReturnRebootCode was set (tells Intune a soft reboot is needed)
      the original non-zero MoveIT code otherwise

.PARAMETER MoveItPath
    Path to moveit.exe. Defaults to moveit.exe beside this script.

.PARAMETER ConfigPath
    Path to a MoveIT JSON config. Defaults to moveit.json beside this script, if present.

.PARAMETER ArgumentList
    Extra arguments passed straight to moveit.exe. Use this instead of -ConfigPath for simple runs,
    for example: -ArgumentList 'migrate','--all','--generate-passwords','--leave-entra'

.PARAMETER RunOnce
    Skip (exit 0) if the done marker already exists. Use this for GPO startup scripts.

.PARAMETER MarkerPath
    The done marker. Defaults to C:\ProgramData\MoveIT\migration.done.

.PARAMETER ReturnRebootCode
    On success, return 3010 so Intune performs a managed reboot. Do not combine with a config
    that also sets "reboot": true.

.EXAMPLE
    .\Invoke-MoveItMigration.ps1 -ConfigPath .\moveit.json

.EXAMPLE
    .\Invoke-MoveItMigration.ps1 -ArgumentList 'migrate','--all','--generate-passwords','--leave-entra' -RunOnce
#>
[CmdletBinding()]
param(
    [string]$MoveItPath = (Join-Path $PSScriptRoot 'moveit.exe'),
    [string]$ConfigPath = (Join-Path $PSScriptRoot 'moveit.json'),
    [string[]]$ArgumentList,
    [switch]$RunOnce,
    [string]$MarkerPath = (Join-Path $env:ProgramData 'MoveIT\migration.done'),
    [switch]$ReturnRebootCode
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$dataDir = Join-Path $env:ProgramData 'MoveIT'
$logDir = Join-Path $dataDir 'Logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$transcript = Join-Path $logDir ("deploy-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))
Start-Transcript -Path $transcript -Append | Out-Null

function Write-Marker([int]$Code) {
    $dir = Split-Path -Parent $MarkerPath
    if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    @{
        completedAt = (Get-Date).ToString('o')
        computer    = $env:COMPUTERNAME
        exitCode    = $Code
        tool        = 'Invoke-MoveItMigration.ps1'
    } | ConvertTo-Json | Set-Content -Path $MarkerPath -Encoding UTF8
}

$exitCode = 1618   # generic deployment failure unless we get further
try {
    if ($RunOnce -and (Test-Path $MarkerPath)) {
        Write-Host "Marker $MarkerPath already present; nothing to do."
        $exitCode = 0
    }
    else {
        if (-not (Test-Path $MoveItPath)) { throw "moveit.exe not found at $MoveItPath." }

        if ($ArgumentList) {
            $moveArgs = @($ArgumentList)
        }
        elseif (Test-Path $ConfigPath) {
            $moveArgs = @('migrate', '--config', $ConfigPath)
        }
        else {
            throw "Provide -ConfigPath to an existing config, or -ArgumentList. Neither was usable."
        }

        if ($moveArgs -notcontains '--yes') { $moveArgs += '--yes' }

        Write-Host "Running: $MoveItPath $($moveArgs -join ' ')"
        & $MoveItPath @moveArgs
        $code = $LASTEXITCODE
        Write-Host "MoveIT exit code: $code"

        if ($code -eq 0 -or $code -eq 2) {
            Write-Marker -Code $code
            if ($ReturnRebootCode) { $exitCode = 3010 } else { $exitCode = 0 }
            Write-Host "Success. Returning $exitCode."
        }
        else {
            Write-Host "MoveIT reported a problem (exit $code). See the MoveIT log in $logDir."
            $exitCode = $code
        }
    }
}
catch {
    Write-Host "Wrapper error: $($_.Exception.Message)"
    $exitCode = 1618
}
finally {
    Stop-Transcript | Out-Null
}

exit $exitCode
