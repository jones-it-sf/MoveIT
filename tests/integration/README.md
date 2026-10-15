# MoveIT integration tests

These run on a real Windows machine and exercise the parts the unit tests cannot: rewriting file
and registry permissions, loading a user's registry hive, creating and cleaning up local accounts,
and repointing the profile so another account takes over an existing profile folder.

## Run the automated tests

Use a **disposable test VM** and take a snapshot first. In an elevated PowerShell session:

```powershell
# Build first (from the repo root, on any OS):
#   dotnet publish src/MoveIT.Cli -c Release -r win-x64   (or win-arm64)

cd tests\integration
powershell -ExecutionPolicy Bypass -File .\Invoke-MoveItTests.ps1
```

Each test creates a throwaway local account, materialises a real profile for it with the Windows
`CreateProfile` API, then migrates that profile to a new local account. This drives the same engine
an Entra ID to local migration uses; only the source account type differs. The script cleans up the
accounts, profiles, registry entries and backups it created unless you pass `-KeepArtifacts`.

Pass `-MoveItPath` to point at a specific `moveit.exe`. The script exits non-zero if any check fails.

## What the automated tests cover

- `--what-if` changes nothing.
- A full migration: the target account is created, the profile folder and registry hive permissions
  reference the new account, no files are copied, the source registration is removed, and a backup
  is written.
- `rollback` restores the source and removes the created account.
- `--disable-source` disables the old local account.
- Pre-flight refuses an unknown source and reports "nothing changed" (exit code 4).
- `list` reports profiles.

## Manual Entra ID test (needs a real tenant)

The automated tests use a local source account because a real Entra profile needs an interactive
cloud sign-in. To validate the Entra path end to end:

1. On an Entra-joined test VM (snapshot first), sign in once as the Entra user to create the profile,
   then sign out and sign in as a different local administrator.
2. `moveit list` should show the Entra profile with type `Entra`.
3. `moveit migrate --source "AzureAD\<user>" --target <name> --create --admin --what-if`.
4. Run it again without `--what-if`. Confirm, then sign in as the new local account and check the
   desktop, documents, Start menu, search, Edge and Outlook.
5. On a fresh snapshot, test `--leave-entra` and `rollback`.

Logs are under `C:\ProgramData\MoveIT\Logs`.
