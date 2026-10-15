#Requires -Version 5.1
<#
.SYNOPSIS
    Audit script for the Iru (formerly Kandji) Windows Custom Script Library Item.

.DESCRIPTION
    Paste this into the "Audit Script" field of a Windows Custom Script Library Item, with
    Remediate-MoveItMigration.ps1 in the "Remediation Script" field and Execution Frequency set to
    "Install once per device".

    The device is "compliant" only once the migration has completed (the done marker exists). Until
    then this exits non-zero, which tells Iru to run the remediation script. Iru reruns this audit on
    every check-in (~15 min) until it passes, giving us the retry loop for free.

    Exit 0 = already migrated (compliant). Exit 1 = not yet migrated (run remediation).
#>

$marker = Join-Path $env:ProgramData 'MoveIT\migration.done'
if (Test-Path $marker) {
    Write-Host "MoveIT migration already completed on this device."
    exit 0
}

Write-Host "MoveIT migration not yet completed."
exit 1
