# Deploying MoveIT with Iru (formerly Kandji)

Iru added Windows management (Iru Endpoint), using Microsoft's MDM framework plus the **Iru Agent**.
The agent can deploy apps and **run custom PowerShell scripts** on enrolled Windows computers, which
is the hook MoveIT uses. You deliver MoveIT with a small bootstrapper script, `Deploy-MoveItViaIru.ps1`,
pasted into an Iru custom-script Library Item.

## Before you start: does your fleet qualify?

Iru's Windows support has real limits. Check these first:

- **OS:** Windows 11 **24H2, 25H2, or 26H2 only**, on **Pro, Pro Education, Enterprise, or Education**.
  Windows 10 and any Home edition cannot be managed by Iru, so this path will not reach them. For
  those, use the RMM or GPO methods in [`../README.md`](../README.md).
- **Agent context:** the Iru Agent runs scripts as SYSTEM. MoveIT needs that. Confirm in your tenant
  if unsure.
- **Architecture:** build the `win-x64` package for Intel/AMD PCs and `win-arm64` for ARM PCs. If your
  fleet is mixed, host one package per architecture and assign them to separate Blueprints.

## How it works

The bootstrapper does not carry the binary itself. It downloads a **package `.zip` you host**, then
runs the same wrapper every other method uses, so behavior (exit codes, the run-once marker) is
identical everywhere.

```
Iru Agent runs Deploy-MoveItViaIru.ps1 (as SYSTEM)
   -> downloads moveit-package.zip from your URL
   -> runs Invoke-MoveItMigration.ps1 -ConfigPath moveit.json -RunOnce
      -> runs moveit.exe, writes C:\ProgramData\MoveIT\migration.done on success
```

## 1. Build the package

Put these three files in a folder and zip them (zip the files, not the folder):

- `moveit.exe` from `dotnet publish src/MoveIT.Cli -c Release -r win-x64` (or `-r win-arm64`)
- `Invoke-MoveItMigration.ps1` from [`../`](../)
- `moveit.json` (your config, e.g. a copy of [`../../samples/migrate-all.json`](../../samples/migrate-all.json))

Then record the hash for the script to verify:

```powershell
(Get-FileHash .\moveit-package.zip -Algorithm SHA256).Hash
```

## 2. Host the package

Put `moveit-package.zip` somewhere the devices can reach over HTTPS: Azure Blob Storage with a SAS
URL, an S3 object, a GitHub release asset (the repo's CI already publishes the binaries as artifacts),
or an internal web server. Copy the URL.

## 3. Create the Iru custom script

In Iru Endpoint, add a **custom script** Library Item for Windows and assign it to your Windows
Blueprint. Paste in `Deploy-MoveItViaIru.ps1` and edit the CONFIG block at the top:

- `$PackageUrl` = your package URL from step 2.
- `$PackageSha256` = the hash from step 1 (recommended).
- `$DeferIfUserLoggedOn` = `$true` (default) so it waits for a device with no one signed in.
- `$ExtraArgs` = leave empty to use `moveit.json`, or pass arguments directly, e.g.
  `@('migrate','--all','--generate-passwords','--leave-entra')`.

The script exits **0** on success, when it defers (a user is logged on), or when the device already
migrated, and **non-zero** on failure, which is what Iru expects from a custom script.

## The thing to plan for: nobody can be signed in

This is the one real constraint of in-place profile migration via any MDM, not specific to Iru.
MoveIT refuses to migrate a profile that is currently loaded, so the target user must be **signed
out** when the script runs. Agent scripts normally run on check-in, while the user is active, so:

- The bootstrapper defaults to **deferring** (exit 0, retry later) whenever any interactive user is
  logged on. On its own, that means it only runs if it happens to check in at the login screen.
- The reliable pattern is a **maintenance window**: schedule a reboot (or have users sign out) for a
  window, so devices sit at the login screen and the next agent check-in migrates them.
- If you want a hands-off "migrate at next boot, before anyone logs in" flow instead of relying on
  catching the login screen, tell me and I will add a startup-staging mode to the wrapper (it installs
  a one-time boot task and triggers a scheduled reboot).

Set the reboot in `moveit.json` (`"reboot": true`) or handle it in your window. Do not use the
wrapper's `-ReturnRebootCode` here; a script runner treats a non-zero (3010) exit as a failure.

## Testing on your UTM VM

If you want to test the Iru path (not just run the exe locally), note Iru enrollment needs a
**stable serial number** on the VM and **TPM 2.0** enabled, and the VM must run a supported Windows 11
build and edition. To just prove the migration works first, you can skip Iru entirely and run the
integration harness in [`../../tests/integration`](../../tests/integration/README.md).

## Collecting results

- Bootstrapper and wrapper logs: `C:\ProgramData\MoveIT\Logs\`
- Done marker: `C:\ProgramData\MoveIT\migration.done`
- If you used `--generate-passwords`, the credentials CSV is under the backup root. Store it securely
  and delete it after handing out passwords.
