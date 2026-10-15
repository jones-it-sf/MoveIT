# MoveIT

MoveIT migrates Windows user profiles to a different account **in place**, without copying any files.
It is built for moving Entra ID (Azure AD) profiles to local accounts, the same job ForensiT's User
Profile Wizard Corporate Edition does, as a single self-contained `moveit.exe` with no per-device fee.

> **Status: not yet validated on Windows.** The logic that can run anywhere is covered by 144 unit
> tests, but the parts that touch real Windows (file and registry permissions, loading a user's
> registry hive, creating accounts, leaving Entra ID) have not been run yet. Test on disposable VMs
> before using it on anything real. See [`tests/integration`](tests/integration/README.md).

## How it works

Windows decides which profile folder an account opens from a registry key called `ProfileList`, keyed
by the account's security identifier (SID). To move a profile to a new account, MoveIT:

1. Grants the new account the old account's permissions on the profile folder and on the user's
   registry files (`NTUSER.DAT` and `UsrClass.dat`), rewriting every access control entry in place.
2. Repoints `ProfileList` so the new account's SID owns the existing folder, and removes the old
   registration.

The registration swap happens **last**, after all permissions are updated and verified, so a failure
part-way through leaves the original user able to sign in exactly as before. Every migration writes a
backup folder that `moveit rollback` uses to undo the change.

An Entra account's SID is derived from the user's directory object ID, so MoveIT can prepare the new
local profile without the Entra user ever signing in again.

## Features

| Capability | How |
| --- | --- |
| Migrate one profile | `moveit migrate --source AzureAD\JohnSmith --target jsmith --create` |
| Migrate every Entra profile on a machine | `moveit migrate --all --generate-passwords` |
| Map users from a spreadsheet | `moveit migrate --map users.csv` |
| Generate target account names automatically | name is derived from the account/UPN, kept unique |
| Create accounts with strong random passwords | `--generate-passwords`, written to a secured CSV |
| Make new accounts administrators | `--admin` |
| Disable or delete the old (local) account | `--disable-source` / `--delete-source` |
| Run your own code after each profile / after all | `--after-each`, `--after-all` (ps1/cmd/bat/exe) |
| Leave Entra ID and reboot when done | `--leave-entra --reboot` |
| Preview without changing anything | `--what-if` |
| Roll a migration back | `moveit rollback --backup <folder>` |
| Push to many machines at once | `moveit push --computers hosts.txt --config x.json` |
| GUI wizard | `MoveIT-Wizard.exe` |
| Config file, logging, command line | JSON config, logs under `C:\ProgramData\MoveIT\Logs` |

## Build

Needs the .NET 10 SDK. From the repo root:

```bash
dotnet test                                                   # build + run unit tests
dotnet publish src/MoveIT.Cli -c Release -r win-x64           # -> moveit.exe
dotnet publish src/MoveIT.Gui -c Release -r win-arm64         # -> MoveIT-Wizard.exe
```

Use `win-arm64` for Windows VMs on Apple Silicon, `win-x64` for Intel/AMD PCs. The CLI is a trimmed
~11 MB single file; the GUI is ~49 MB (WinForms cannot be trimmed). Both are self-contained and need
nothing installed on the target PC.

## Usage

Run elevated (Run as administrator), or as SYSTEM from a deployment tool. The users being migrated
must be signed out. `moveit help` lists every option.

```powershell
# Preview migrating one Entra user to a new local admin account:
moveit migrate --source "AzureAD\JohnSmith" --target jsmith --create --admin --what-if

# Migrate every Entra profile, generate passwords, leave Entra, restart:
moveit migrate --all --generate-passwords --admin --leave-entra --reboot --yes

# From a config file:
moveit migrate --config samples\migrate-all.json --yes

# Undo it:
moveit rollback --backup "C:\ProgramData\MoveIT\Backups\20261015-091500_jsmith"
```

Sample configs are in [`samples/`](samples/). Exit codes: `0` success, `1` failed, `2` succeeded with
warnings, `3` bad arguments, `4` pre-flight failed or cancelled (nothing changed).

## Deploying at scale

The [`deploy/`](deploy/README.md) folder has a reusable wrapper plus instructions for Intune, RMM
tools, and Group Policy. `moveit push` can also drive remote machines directly over SMB and the Task
Scheduler.

## What does not carry over

This is true of any in-place migration, including ForensiT's: data tied to the old account's
credentials usually does not survive. That includes some saved browser passwords, Windows Credential
Manager entries, and Windows Hello PINs. Store apps or the Start menu occasionally need to
re-register on first sign-in.

## Project layout

```
src/MoveIT.Core    Migration engine, Win32 interop, SID/ACL logic (the library)
src/MoveIT.Cli     moveit.exe command-line tool
src/MoveIT.Gui     MoveIT-Wizard.exe WinForms wizard
tests/MoveIT.Core.Tests   unit tests
tests/integration         PowerShell end-to-end tests for a Windows lab
deploy             Intune / RMM / GPO deployment kit
samples            example config files and a mapping CSV
```

## Legal

MoveIT is an independent implementation written from documented Windows behavior. It is not
affiliated with ForensiT and contains none of their code.
