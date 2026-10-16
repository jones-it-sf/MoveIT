# Iru Windows Custom App deployment

This is the recommended production deployment for MoveIT. Iru supplies the user-facing enforcement
deadline, **Update now** button, one-hour deferrals, and final countdown. After enforcement, the
MoveIT installer logs off interactive Windows sessions, waits for their profile hives to unload, and
runs the migration as SYSTEM.

## User experience

1. **MoveIT Profile Migration** appears in the Iru system tray as a pending app.
2. The user can start it with **Update now**, or keep working until your enforcement deadline.
3. At the deadline, Iru shows its five-minute enforcement countdown. The user can delay one hour at
   a time, up to Iru's 24-hour total limit.
4. When installation starts, Iru closes Explorer and `Install-MoveItCustomApp.ps1` explicitly logs
   off all interactive sessions. Closing Explorer alone is not a Windows logoff and does not unload
   `NTUSER.DAT`.
5. The installer waits until every interactive session is gone, runs MoveIT as SYSTEM, and writes
   `C:\ProgramData\MoveIT\migration.done`. Iru uses that file as its detection rule.

If migration fails, the marker is not written. Iru reports the installation failure and retries
according to its normal Custom App behavior. MoveIT v0.1.1 and later also check profile-hive locks
during preflight, before creating an account or changing permissions.

## 1. Download the package

Download the package for the device architecture from the latest GitHub release:

- Intel/AMD: `moveit-iru-custom-app-win-x64.zip`
- ARM: `moveit-iru-custom-app-win-arm64.zip`

The ZIP contains:

- `moveit.exe`
- `Install-MoveItCustomApp.ps1`

Do not unpack and re-zip it unless you need to customize the installer defaults. Iru accepts the ZIP
directly.

## 2. Create the Iru Library Item

In Iru Endpoint, create a **Windows Custom App** Library Item and use these settings:

- **Name:** `MoveIT Profile Migration`
- **Installer:** upload the architecture-appropriate ZIP.
- **Installation:** `Install once per device`.
- **Install context:** System.
- **Install command** (pilot, keep the device Entra-joined):

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-MoveItCustomApp.ps1
  ```

- **Install command** (production, create local administrators, leave Entra, and restart):

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-MoveItCustomApp.ps1 -MakeAdmin -LeaveEntra -RestartAfterSuccess
  ```

- **Open app / blocking executable:** `explorer.exe`.
- **Detection rule:** file exists at `C:\ProgramData\MoveIT\migration.done`.
- **Enforcement:** By enforcement deadline. Pick the grace period and local deadline time appropriate
  for the client.
- **Uninstall:** MoveIT has nothing to uninstall. If Iru requires a command, use:

  ```cmd
  cmd.exe /c exit 0
  ```

Use a separate Library Item or Blueprint for ARM devices because the package architecture differs.

## 3. Pilot safely

Start with one disposable or snapshotted device:

1. Use the pilot install command without `-LeaveEntra`.
2. Assign the Library Item only to the pilot Blueprint/device.
3. While signed in, confirm MoveIT appears in the Iru tray and the enforcement alert is displayed.
4. Select **Update now**. Expect the Windows session to log off.
5. In Iru, confirm the Custom App succeeds and its detection rule passes.
6. Sign in as the generated local account and verify the original desktop and data.

The generated password CSV is stored under `C:\ProgramData\MoveIT\Backups\`. Retrieve it securely and
delete it after delivering the password. Logs are in `C:\ProgramData\MoveIT\Logs\`.

Only after that pilot succeeds should you test `-LeaveEntra -RestartAfterSuccess`.

## Recovery and retries

- A failed run does not write the detection marker, so Iru can retry it.
- MoveIT prints the exact backup directory and rollback command on failure.
- If a previous attempt created the target account but stopped before profile reassignment, a retry
  reuses that account.
- Do not sign in to a newly created target account after a partial run. Doing so creates a separate
  target profile registration and requires `--overwrite-target-profile` or cleanup before retrying.

## Alternative: Custom Script

[`README.md`](README.md) documents the older audit/remediation Custom Script path. It is useful for
unattended maintenance windows and testing, but it cannot provide the Windows Custom App enforcement
and deferral interface.
