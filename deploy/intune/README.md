# Deploying MoveIT with Microsoft Intune

Package MoveIT as a Win32 app so Intune pushes it to Entra-joined devices and runs it as SYSTEM.

## 1. Build the payload

Put these three files in one folder, for example `C:\MoveItPackage\`:

- `moveit.exe` (from `dotnet publish src/MoveIT.Cli -c Release -r win-x64`, or `win-arm64`)
- `Invoke-MoveItMigration.ps1` (from `deploy/`)
- `moveit.json` (your config, for example a copy of `samples/migrate-all.json`)

## 2. Wrap it as .intunewin

Download the Microsoft Win32 Content Prep Tool, then:

```
IntuneWinAppUtil.exe -c C:\MoveItPackage -s Invoke-MoveItMigration.ps1 -o C:\MoveItOutput
```

## 3. Create the Win32 app in Intune

- Install command:
  ```
  powershell.exe -ExecutionPolicy Bypass -File Invoke-MoveItMigration.ps1 -ConfigPath moveit.json -RunOnce
  ```
  Add `-ReturnRebootCode` if you want Intune to handle the reboot (and remove `"reboot": true`
  from the config). The wrapper returns 3010 for a managed reboot.
- Uninstall command (required by Intune, but MoveIT has nothing to uninstall):
  ```
  cmd /c echo nothing to uninstall
  ```
- Install behavior: **System**.
- Return codes: keep the defaults (0 = success, 3010 = soft reboot).
- Detection rule: **Use a custom detection script**, upload `Detect-MoveItMigration.ps1`.
  Run as 32-bit: No.

## 4. Assign and watch

Assign to a test device group first. The wrapper logs to `C:\ProgramData\MoveIT\Logs`, and MoveIT's
own log sits beside it. The done marker is `C:\ProgramData\MoveIT\migration.done`.

## Notes

- Target only devices whose users are signed out, or schedule it for a maintenance window. MoveIT
  refuses to migrate a profile that is still loaded.
- If the config sets `leaveEntra: true`, the device drops out of Entra ID (and should be retired in
  Intune). Make sure at least one local administrator remains, or the migration will refuse to run.
- `-RunOnce` means a device that already migrated is left alone even if the app stays assigned.
