using System.Runtime.Versioning;
using MoveIT.Core.Accounts;
using MoveIT.Core.Acl;
using MoveIT.Core.Logging;
using MoveIT.Core.Profiles;
using MoveIT.Core.Security;

namespace MoveIT.Core.Migration;

/// <summary>Undoes the recorded steps of one profile migration. Leaving Entra ID cannot be undone.</summary>
[SupportedOSPlatform("windows")]
public static class RollbackRunner
{
    public static void Run(string backupDirectory, bool deleteCreatedAccount, Logger log)
    {
        if (!Environment.IsPrivilegedProcess)
        {
            throw new MigrationException("MoveIT must run elevated. Open the terminal with 'Run as administrator', or run it as SYSTEM.");
        }

        Privileges.EnableMigrationPrivileges();
        var backup = BackupSession.Load(backupDirectory);
        var manifest = backup.Manifest;

        if (manifest.RolledBack)
        {
            log.Warn("This migration has already been rolled back.");
            return;
        }

        if (!manifest.ComputerName.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            throw new MigrationException($"This backup was made on {manifest.ComputerName}, not on {Environment.MachineName}.");
        }

        var source = Sid.Parse(manifest.SourceSid);
        Sid? target = manifest.TargetSid is null ? null : Sid.Parse(manifest.TargetSid);
        var sourceName = manifest.SourceAccount ?? manifest.SourceSid;
        log.Info($"Rolling back {sourceName} -> {manifest.TargetAccount} (started {manifest.StartedAt:yyyy-MM-dd HH:mm}).");

        if (target is not null && ProfileStore.IsLoaded(target))
        {
            throw new MigrationException($"'{manifest.TargetAccount}' is signed in. Sign them out first.");
        }

        if (ProfileStore.IsLoaded(source))
        {
            throw new MigrationException($"{sourceName} is signed in. Sign them out first.");
        }

        if (manifest.HasStep(MigrationSteps.ProfileListReassigned))
        {
            if (target is not null && ProfileStore.DeleteEntryIfPathMatches(target, manifest.ProfilePath))
            {
                log.Info($"Removed the profile registration of '{manifest.TargetAccount}'.");
            }

            backup.ImportRegistryFile(BackupSession.SourceProfileListFile);
            log.Success($"Restored the profile registration of {sourceName}.");

            if (backup.HasFile(BackupSession.SourceProfileGuidFile))
            {
                backup.ImportRegistryFile(BackupSession.SourceProfileGuidFile);
            }

            if (manifest.TargetProfileEntryReplaced && backup.HasFile(BackupSession.TargetProfileListFile))
            {
                backup.ImportRegistryFile(BackupSession.TargetProfileListFile);
                log.Info($"Restored the previous profile registration of '{manifest.TargetAccount}'.");
            }
        }

        if (manifest.HasStep(MigrationSteps.SourceAclRemovalStarted) && target is not null)
        {
            log.Info($"Restoring the permissions of {sourceName}...");
            new FileSystemAclMigrator(target, source, AclMode.Add, dryRun: false, log).Run(manifest.ProfilePath);
            foreach (var hive in ProfileStore.GetHivePaths(manifest.ProfilePath).Where(File.Exists))
            {
                new RegistryHiveAclMigrator(target, source, AclMode.Add, dryRun: false, log).Run(hive);
            }
        }

        if (manifest.TargetAddedToAdministrators && target is not null)
        {
            LocalAccounts.RemoveFromAdministrators(target);
            log.Info($"Removed '{manifest.TargetAccount}' from the local Administrators group.");
        }

        if (manifest.TargetCreated)
        {
            if (deleteCreatedAccount)
            {
                LocalAccounts.Delete(manifest.TargetAccount);
                log.Info($"Deleted local account '{manifest.TargetAccount}'.");
            }
            else
            {
                log.Info($"Local account '{manifest.TargetAccount}' was created by the migration and has been kept. Add --delete-created-account to remove it.");
            }
        }

        manifest.RolledBack = true;
        backup.Save();
        log.Success($"Rollback complete. The profile is registered to {sourceName} again.");
        if (source.IsEntraUser)
        {
            log.Info("If this device has left Entra ID, rejoin it before that account can sign in.");
        }
    }
}
