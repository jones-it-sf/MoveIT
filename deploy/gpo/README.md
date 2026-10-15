# Deploying MoveIT with Group Policy (startup script)

A computer **startup** script runs as SYSTEM before anyone signs in, which is ideal for profile
migration because no user profile is loaded yet.

> Group Policy applies to Active Directory domain members. A device that is only Entra-joined is not
> a GPO target. Use Intune or an RMM for Entra-only devices. GPO fits hybrid-joined devices.

## 1. Copy the payload to a share every computer can read

For example `\\contoso\netlogon\MoveIT\`:

- `moveit.exe`
- `Invoke-MoveItMigration.ps1`
- `moveit.json`

## 2. Create the startup script

In a GPO: **Computer Configuration > Policies > Windows Settings > Scripts > Startup >
PowerShell Scripts**. Add a script with:

- Script Name:
  ```
  \\contoso\netlogon\MoveIT\Invoke-MoveItMigration.ps1
  ```
- Script Parameters:
  ```
  -ConfigPath \\contoso\netlogon\MoveIT\moveit.json -RunOnce
  ```

`-RunOnce` writes `C:\ProgramData\MoveIT\migration.done` on success and skips the device on later
boots, so the migration runs exactly once per machine.

## 3. Scope and test

Link the GPO to an OU with one test computer first. Restart it and check
`C:\ProgramData\MoveIT\Logs` and the done marker. When satisfied, widen the scope.

## Reboot

A startup script already runs at boot, so you usually do not need `"reboot": true`. If the migration
leaves Entra ID, a second restart helps Windows settle; you can schedule that separately.
