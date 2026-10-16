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

## 1. Cut a release (CI builds the package)

The package is hosted as a **GitHub release asset**, built automatically by CI. Push a version tag
and the workflow publishes both architecture packages and attaches them to the release:

```bash
git tag v0.1.0
git push origin v0.1.0
```

That produces, on the release:

- `moveit-win-x64.zip` and `moveit-win-arm64.zip`, each containing `moveit.exe` and
  `Invoke-MoveItMigration.ps1` (binary only; no config, so nothing sensitive is public),
- `MoveIT-Wizard-win-x64.exe` and `MoveIT-Wizard-win-arm64.exe` (the GUI, for manual use).

The remediation script downloads from `releases/latest/download/moveit-win-x64.zip`, so it always
gets the newest release without editing the URL. To pin a version instead, point `$PackageUrl` at a
specific tag URL and set `$PackageSha256` (get the hash from `Get-FileHash .\moveit-win-x64.zip
-Algorithm SHA256`).

Because the package is binary-only, the migration options live in the remediation script's
`$ExtraArgs`, not in a bundled config.

## 2. Create the Windows Custom Script Library Item

In Iru Endpoint, add a **Custom Script** Library Item for Windows, assign it to your Windows
Blueprint, and set:

- **Execution Frequency:** Install once per device.
- **Audit Script:** paste [`Audit-MoveItMigration.ps1`](Audit-MoveItMigration.ps1) as-is.
- **Remediation Script:** paste [`Remediate-MoveItMigration.ps1`](Remediate-MoveItMigration.ps1) and
  set `$ExtraArgs` to the migration you want (it defaults to
  `@('migrate','--all','--generate-passwords')`). `$PackageUrl` already points at this repo's latest
  release.
- **Restart after a successful execution:** enable this if the migration should reboot (recommended if
  you add `--leave-entra` to `$ExtraArgs`). Users get a 30-minute countdown.

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
