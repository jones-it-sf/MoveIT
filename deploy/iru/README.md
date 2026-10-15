# Deploying MoveIT with Iru (formerly Kandji)

Iru added Windows management (Iru Endpoint), using Microsoft's MDM framework plus the **Iru Agent**.
The agent runs PowerShell on Windows through a **Custom Script Library Item** that uses an
**audit + remediation** model, and MoveIT plugs straight into it. The agent, not our script, handles
the retry loop, the SYSTEM context, status reporting, and the post-migration reboot.

## Why the audit/remediation model fits

A Windows Custom Script Library Item has two scripts and an execution frequency:

- The **audit script** decides whether the device is already in the desired state.
- The **remediation script** runs, as SYSTEM, whenever the audit says it is not.
- With frequency **"Install once per device"**, Iru **reruns the audit on every check-in (about every
  15 minutes) until it passes**, running remediation each time it fails.

That gives us exactly what in-place migration needs for free:

- **Deferral until no one is signed in.** MoveIT cannot migrate a loaded profile, so the remediation
  script does nothing (exits `10`) while a user is logged on. Iru simply tries again next check-in, so
  a device migrates the first time it checks in with no one signed in. No maintenance window required.
- **A built-in retry loop** until the migration actually succeeds (the audit keeps failing until the
  done marker exists).
- **A native reboot.** Turn on "Restart after a successful execution" and the user gets the agent's
  30-minute restart countdown when remediation succeeds. Keep `"reboot": false` in `moveit.json` so
  MoveIT does not reboot on its own.
- **Progress tracking.** In the Iru console, **Pass = migrated**, an alert with a "DEFERRED" message =
  waiting for the user to sign out, and any other alert = a real failure (the output and the MoveIT
  log tell you which).

## Before you start: does your fleet qualify?

- **OS:** Windows 11 **24H2, 25H2, or 26H2 only**, on **Pro, Pro Education, Enterprise, or Education**.
  Windows 10 and Home editions cannot be managed by Iru; use the RMM or GPO path in
  [`../README.md`](../README.md) for those.
- **Architecture:** build the `win-x64` package for Intel/AMD and `win-arm64` for ARM. For a mixed
  fleet, host one package per architecture and assign them to separate Blueprints.
- Scripts run at SYSTEM, which is what MoveIT needs.

## 1. Build and host the package

Zip these three files together (zip the files, not the folder):

- `moveit.exe` from `dotnet publish src/MoveIT.Cli -c Release -r win-x64` (or `-r win-arm64`)
- `Invoke-MoveItMigration.ps1` from [`../`](../)
- `moveit.json` (your config, e.g. a copy of [`../../samples/migrate-all.json`](../../samples/migrate-all.json),
  with `"reboot": false` so Iru owns the reboot)

Record the hash and host the `.zip` somewhere the devices can reach over HTTPS (Azure Blob + SAS, S3,
a GitHub release asset, or an internal web server):

```powershell
(Get-FileHash .\moveit-package.zip -Algorithm SHA256).Hash
```

## 2. Create the Windows Custom Script Library Item

In Iru Endpoint, add a **Custom Script** Library Item for Windows, assign it to your Windows
Blueprint, and set:

- **Execution Frequency:** Install once per device.
- **Audit Script:** paste [`Audit-MoveItMigration.ps1`](Audit-MoveItMigration.ps1) as-is.
- **Remediation Script:** paste [`Remediate-MoveItMigration.ps1`](Remediate-MoveItMigration.ps1) and
  edit its CONFIG block (`$PackageUrl`, `$PackageSha256`, and optionally `$ExtraArgs`).
- **Restart after a successful execution:** enable this if the migration should reboot (recommended
  when `leaveEntra` is set). Users get a 30-minute countdown.

Each custom script has a 1-hour execution timeout, which is far more than a migration needs.

## 3. Roll out and watch

Assign to a pilot device group first. In the console, devices move to **Pass** as they migrate.
Anything stuck shows its output: a "DEFERRED" message means it is just waiting for the user to sign
out, anything else is a failure to investigate.

- Logs on the device: `C:\ProgramData\MoveIT\Logs\`
- Done marker: `C:\ProgramData\MoveIT\migration.done`
- If you used `--generate-passwords`, the credentials CSV is under the backup root. Store it securely
  and delete it after handing out passwords.

## Testing on your UTM VM

To test the Iru path end to end, enrollment needs a **stable serial number** on the VM and **TPM 2.0**
enabled, on a supported Windows 11 build and edition. To just prove the migration itself works first,
skip Iru and run the integration harness in
[`../../tests/integration`](../../tests/integration/README.md).
