# Deploying MoveIT at scale

MoveIT is a single self-contained `moveit.exe`. To migrate many machines, push the exe plus a config
and run it as SYSTEM. Every method below uses the same wrapper, `Invoke-MoveItMigration.ps1`, which:

- finds `moveit.exe` and `moveit.json` next to itself,
- runs the migration non-interactively (`--yes`),
- writes `C:\ProgramData\MoveIT\migration.done` on success (so a run-once guard and detection work),
- maps MoveIT's exit codes to codes deployment tools understand (0 success, 3010 optional reboot).

## Pick a method

| Device type | Method | Folder |
| --- | --- | --- |
| Entra-joined | Intune Win32 app | `intune/` |
| Hybrid / AD-joined | Group Policy startup script | `gpo/` |
| Any, via your RMM | Run the wrapper as a SYSTEM script | below |

## RMM (NinjaOne, Datto, ConnectWise, Action1, etc.)

Most RMMs can run a PowerShell script as SYSTEM and ship supporting files. Ship `moveit.exe`,
`Invoke-MoveItMigration.ps1` and your `moveit.json` to a folder, then run:

```powershell
powershell.exe -ExecutionPolicy Bypass -File Invoke-MoveItMigration.ps1 -ConfigPath moveit.json -RunOnce
```

Or skip the config and pass arguments directly:

```powershell
powershell.exe -ExecutionPolicy Bypass -File Invoke-MoveItMigration.ps1 `
  -ArgumentList 'migrate','--all','--generate-passwords','--leave-entra' -RunOnce
```

The RMM will see exit code 0 for success and a non-zero code for failure. Collect
`C:\ProgramData\MoveIT\Logs\*.log` and, if you used `--generate-passwords`, the credentials CSV under
the backup root (store it securely and delete it after handing out passwords).

## Before you roll out widely

- Test on a snapshot of a real machine first, and read `../tests/integration/README.md`.
- Users must be signed out; MoveIT refuses to migrate a loaded profile.
- If you leave Entra ID, keep one enabled local administrator, and retire the device in Intune.
