using System.ComponentModel;
using System.Runtime.Versioning;
using MoveIT.Core.Accounts;
using MoveIT.Core.Acl;
using MoveIT.Core.Entra;
using MoveIT.Core.Logging;
using MoveIT.Core.Profiles;
using MoveIT.Core.Security;
using MoveIT.Core.Util;

namespace MoveIT.Core.Migration;

/// <summary>
/// Moves existing profile folders to local accounts in place: no files are copied. For each profile it
/// grants the target account the source's permissions on the folder and both registry hives, then
/// re-points the profile registration. The registration swap happens last, so a failure part-way leaves
/// the original user able to sign in as before.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MigrationEngine(MigrationConfig config, Logger log, Func<string, string?>? passwordPrompt)
{
    private static readonly Sid WhatIfPlaceholderTarget = Sid.Parse("S-1-5-21-0-0-0-1000");

    /// <summary>Runs every pre-flight check without changing anything. Throws MigrationException listing all problems.</summary>
    public MigrationPlan Prepare(bool whatIf)
    {
        var configErrors = config.Validate(Environment.MachineName);
        if (configErrors.Count > 0)
        {
            throw new MigrationException("The migration settings are not valid.", configErrors);
        }

        if (!Environment.IsPrivilegedProcess)
        {
            throw new MigrationException("MoveIT must run elevated. Open the terminal with 'Run as administrator', or run it as SYSTEM from your RMM tool.");
        }

        Privileges.EnableMigrationPrivileges();

        var errors = new List<string>();
        var warnings = new List<string>();
        var profiles = ProfileStore.GetProfiles(includeSystem: false);
        var planned = new List<PlannedProfile>();

        foreach (var mapping in config.Profiles)
        {
            if (PlanProfile(mapping, profiles, whatIf, errors, warnings) is { } profile)
            {
                planned.Add(profile);
            }
        }

        foreach (var duplicate in planned.GroupBy(p => p.Source.Sid).Where(g => g.Count() > 1))
        {
            errors.Add($"More than one mapping uses the profile of {duplicate.First().Source.DisplayName}.");
        }

        var sourceSids = planned.Select(p => p.Source.Sid).ToHashSet();
        foreach (var profile in planned.Where(p => p.TargetSid is not null && sourceSids.Contains(p.TargetSid)))
        {
            errors.Add($"'{profile.TargetName}' is used as both a source and a target.");
        }

        EntraJoinStatus? joinStatus = null;
        bool leaveEntra = false;
        if (config.LeaveEntra)
        {
            (joinStatus, leaveEntra) = PlanEntraLeave(planned, errors, warnings);
        }

        if (errors.Count > 0)
        {
            throw new MigrationException("Pre-flight checks failed.", errors);
        }

        return new MigrationPlan(planned, joinStatus, leaveEntra, warnings);
    }

    public MigrationReport Execute(MigrationPlan plan, bool whatIf)
    {
        var results = plan.Profiles.Select(profile => whatIf ? Simulate(profile) : Migrate(profile)).ToList();
        bool allSucceeded = results.All(r => r.Succeeded);
        bool leftEntra = false, entraFailed = false, rebootScheduled = false;

        if (plan.LeaveEntra)
        {
            if (whatIf)
            {
                log.Info("[what-if] Would remove this device from Entra ID (dsregcmd /leave).");
            }
            else if (!allSucceeded)
            {
                log.Warn("Skipping the Entra ID leave step because a profile migration failed.");
            }
            else
            {
                try
                {
                    EntraJoin.Leave(log);
                    leftEntra = true;
                }
                catch (Exception e) when (e is MigrationException or Win32Exception or TimeoutException or InvalidOperationException)
                {
                    log.Error(e.Message);
                    entraFailed = true;
                }
            }
        }

        if (config.Reboot)
        {
            if (whatIf)
            {
                log.Info("[what-if] Would restart the computer.");
            }
            else if (allSucceeded && !entraFailed)
            {
                rebootScheduled = ScheduleReboot();
            }
            else
            {
                log.Warn("Not restarting because the migration did not complete cleanly.");
            }
        }

        return new MigrationReport(results, leftEntra, entraFailed, rebootScheduled);
    }

    private PlannedProfile? PlanProfile(ProfileMapping mapping, IReadOnlyList<ProfileEntry> profiles, bool whatIf, List<string> errors, List<string> warnings)
    {
        var label = $"{mapping.Source} -> {mapping.Target}";
        var source = ProfileResolver.Resolve(mapping.Source, profiles, out var resolveError);
        if (source is null)
        {
            errors.Add($"{label}: {resolveError}");
            return null;
        }

        int errorsBefore = errors.Count;

        if (profiles.Any(p => p.IsBackupEntry && p.Sid == source.Sid))
        {
            errors.Add($"{label}: the profile registry has a duplicate '.bak' entry for {source.DisplayName}, which usually means Windows is giving this user a temporary profile. Fix that first.");
        }

        if (source.IsLoaded)
        {
            errors.Add($"{label}: {source.DisplayName} is signed in or their profile is still loaded. Sign them out or restart the computer, then try again.");
        }

        if (!Directory.Exists(source.ProfileImagePath))
        {
            errors.Add($"{label}: the profile folder {source.ProfileImagePath} does not exist.");
        }
        else if (!File.Exists(Path.Combine(source.ProfileImagePath, "NTUSER.DAT")))
        {
            errors.Add($"{label}: {source.ProfileImagePath} has no NTUSER.DAT, so it is not a complete profile.");
        }

        LocalAccountName.TryNormalize(mapping.Target, Environment.MachineName, out var targetName, out _);
        var targetSid = LocalAccounts.TryGetSid(targetName);
        ProfileEntry? targetProfile = null;

        if (targetSid is null)
        {
            if (!mapping.CreateTarget)
            {
                errors.Add($"{label}: local account '{targetName}' does not exist. Add --create (or \"createTarget\": true) to create it.");
            }
        }
        else
        {
            if (targetSid == source.Sid)
            {
                errors.Add($"{label}: the source and target are the same account.");
            }

            if (mapping.CreateTarget)
            {
                warnings.Add($"{label}: local account '{targetName}' already exists and will be used as is.");
            }

            targetProfile = profiles.FirstOrDefault(p => !p.IsBackupEntry && p.Sid == targetSid);
            if (targetProfile is not null)
            {
                if (targetProfile.IsLoaded)
                {
                    errors.Add($"{label}: '{targetName}' is signed in. Sign them out first.");
                }
                else if (!mapping.OverwriteTargetProfile)
                {
                    errors.Add($"{label}: '{targetName}' already has a profile at {targetProfile.ProfileImagePath}. Add --overwrite-target-profile (or \"overwriteTargetProfile\": true) to replace it; the old folder stays on disk.");
                }
                else
                {
                    warnings.Add($"{label}: the existing profile of '{targetName}' at {targetProfile.ProfileImagePath} will be detached. The folder is kept on disk.");
                }
            }
        }

        string? password = null;
        if (targetSid is null && mapping.CreateTarget && !whatIf && errors.Count == errorsBefore)
        {
            password = ResolvePassword(mapping, targetName, label, errors, warnings);
        }

        return errors.Count == errorsBefore
            ? new PlannedProfile(mapping, source, targetName, targetSid, targetProfile, password)
            : null;
    }

    private string? ResolvePassword(ProfileMapping mapping, string targetName, string label, List<string> errors, List<string> warnings)
    {
        if (!string.IsNullOrEmpty(mapping.Password))
        {
            warnings.Add($"{label}: the password is stored in plain text in the config file. Prefer passwordEnv.");
            return mapping.Password;
        }

        if (mapping.PasswordEnv is { } variable)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrEmpty(value))
            {
                errors.Add($"{label}: environment variable {variable} is not set or is empty.");
            }
            return value;
        }

        var prompted = passwordPrompt?.Invoke(targetName);
        if (string.IsNullOrEmpty(prompted))
        {
            errors.Add($"{label}: no password was provided for the new account '{targetName}'. Use --password-env (or \"passwordEnv\") or run interactively.");
        }
        return prompted;
    }

    private (EntraJoinStatus? Status, bool Leave) PlanEntraLeave(IReadOnlyList<PlannedProfile> planned, List<string> errors, List<string> warnings)
    {
        EntraJoinStatus status;
        try
        {
            status = EntraJoin.GetStatus();
        }
        catch (Exception e) when (e is Win32Exception or TimeoutException or InvalidOperationException)
        {
            errors.Add($"Could not read the Entra ID join state: {e.Message}");
            return (null, false);
        }

        if (!status.AzureAdJoined)
        {
            warnings.Add("This device is not joined to Entra ID, so the leave step will be skipped.");
            return (status, false);
        }

        if (status.DomainJoined)
        {
            warnings.Add("This device is hybrid joined (on-premises AD and Entra ID). Windows re-registers hybrid devices automatically unless the computer is removed from Entra Connect sync scope.");
        }

        if (EntraJoin.IsMdmEnrolled())
        {
            warnings.Add("This device is enrolled in Intune. Retire or delete it in Intune as well, or its policies and device record will remain.");
        }

        bool adminWillExist = LocalAccounts.GetEnabledLocalAdministrators().Count > 0 || planned.Any(p => p.Mapping.MakeAdmin);
        if (!adminWillExist)
        {
            const string message = "After leaving Entra ID no enabled local administrator account would remain, and Entra accounts can no longer sign in. Add --admin (or \"makeAdmin\": true) to a profile, or enable a local administrator first.";
            if (config.Force)
            {
                warnings.Add(message + " Continuing because force is set.");
            }
            else
            {
                errors.Add(message + " Use force to override.");
            }
        }

        return (status, true);
    }

    private ProfileResult Simulate(PlannedProfile profile)
    {
        var source = profile.Source;
        var target = profile.TargetSid ?? WhatIfPlaceholderTarget;
        log.Info("");
        log.Info($"=== [what-if] {source.DisplayName} -> {profile.TargetName} ===");

        try
        {
            var files = new FileSystemAclMigrator(source.Sid, target, AclMode.Add, dryRun: true, log).Run(source.ProfileImagePath);
            var registry = UpdateHives(source.ProfileImagePath, source.Sid, target, AclMode.Add, dryRun: true);

            if (profile.CreatesTarget)
            {
                log.Info($"[what-if] Would create local account '{profile.TargetName}'{(profile.Mapping.MakeAdmin ? " and add it to Administrators" : "")}.");
            }
            log.Info($"[what-if] {files.Changed:N0} of {files.Scanned:N0} files and folders and {registry.Changed:N0} of {registry.Scanned:N0} registry keys reference {source.DisplayName} and would be granted to '{profile.TargetName}'.");
            log.Info($"[what-if] Would register {source.ProfileImagePath} as the profile of '{profile.TargetName}' and remove the registration of {source.DisplayName}.");
            if (files.Failed + registry.Failed > 0)
            {
                log.Warn($"[what-if] {files.Failed + registry.Failed:N0} items could not be read. See the warnings above.");
            }

            return new ProfileResult(source.DisplayName, profile.TargetName, true, null, files, registry, null);
        }
        catch (Exception e)
        {
            log.Error($"{source.DisplayName}: {e.Message}");
            log.Debug(e.ToString());
            return new ProfileResult(source.DisplayName, profile.TargetName, false, null, AclMigrationStats.Empty, AclMigrationStats.Empty, e.Message);
        }
    }

    private ProfileResult Migrate(PlannedProfile profile)
    {
        var source = profile.Source;
        var label = $"{source.DisplayName} -> {profile.TargetName}";
        var files = AclMigrationStats.Empty;
        var registry = AclMigrationStats.Empty;
        BackupSession? backup = null;

        log.Info("");
        log.Info($"=== {label} ===");

        try
        {
            backup = BackupSession.Create(config.BackupRoot ?? AppDirectories.DefaultBackups, profile.TargetName, new MigrationManifest
            {
                ToolVersion = ToolInfo.Version,
                ComputerName = Environment.MachineName,
                StartedAt = DateTimeOffset.Now,
                SourceSid = source.Sid.Value,
                SourceAccount = source.AccountName,
                SourceProfileGuid = source.ProfileGuid?.ToString("B"),
                ProfilePath = source.ProfileImagePath,
                TargetAccount = profile.TargetName,
                TargetSid = profile.TargetSid?.Value,
                AclMode = config.AclMode,
            });
            log.Info($"Backup folder: {backup.Directory}");

            EnsureNotLoaded(source.Sid, source.DisplayName);
            var target = PrepareTargetAccount(profile, backup);

            backup.ExportRegistryKey(ProfileStore.ProfileListKey(source.Sid), BackupSession.SourceProfileListFile);
            if (source.ProfileGuid is { } sourceGuid && ProfileStore.ProfileGuidExists(sourceGuid))
            {
                backup.ExportRegistryKey(ProfileStore.ProfileGuidKey(sourceGuid), BackupSession.SourceProfileGuidFile);
            }
            if (profile.TargetExistingProfile is not null)
            {
                backup.ExportRegistryKey(ProfileStore.ProfileListKey(target), BackupSession.TargetProfileListFile);
            }
            backup.MarkStep(MigrationSteps.RegistryBackedUp);

            backup.MarkStep(MigrationSteps.AclUpdateStarted);
            log.Info($"Granting '{profile.TargetName}' access to {source.ProfileImagePath}...");
            files = new FileSystemAclMigrator(source.Sid, target, AclMode.Add, dryRun: false, log).Run(source.ProfileImagePath);
            log.Info($"Files and folders: {files.Changed:N0} updated, {files.Scanned:N0} scanned, {files.Failed:N0} failed, {files.SkippedLinks:N0} links not followed ({files.Elapsed.TotalSeconds:0.0}s).");
            backup.MarkStep(MigrationSteps.FileAclsUpdated);
            VerifyCriticalFiles(source.ProfileImagePath, target);

            log.Info("Updating the user's registry hives...");
            registry = UpdateHives(source.ProfileImagePath, source.Sid, target, AclMode.Add, dryRun: false);
            log.Info($"Registry keys: {registry.Changed:N0} updated, {registry.Scanned:N0} scanned, {registry.Failed:N0} failed ({registry.Elapsed.TotalSeconds:0.0}s).");
            backup.MarkStep(MigrationSteps.HiveAclsUpdated);

            EnsureNotLoaded(source.Sid, source.DisplayName);
            EnsureNotLoaded(target, profile.TargetName);
            ProfileStore.Reassign(source.Sid, target, replaceExistingTarget: profile.TargetExistingProfile is not null);
            backup.Manifest.TargetProfileEntryReplaced = profile.TargetExistingProfile is not null;
            backup.MarkStep(MigrationSteps.ProfileListReassigned);
            if (source.ProfileGuid is { } guid)
            {
                ProfileStore.DeleteProfileGuid(guid, source.Sid);
            }
            log.Success($"{source.ProfileImagePath} is now the profile of {Environment.MachineName}\\{profile.TargetName}.");

            if (config.AclMode == AclMode.Replace)
            {
                RemoveSourceAccess(profile, target, backup);
            }

            backup.Manifest.CompletedAt = DateTimeOffset.Now;
            backup.Save();
            return new ProfileResult(source.DisplayName, profile.TargetName, true, backup.Directory, files, registry, null);
        }
        catch (Exception e)
        {
            log.Error($"{label}: {e.Message}");
            log.Debug(e.ToString());
            if (backup is not null)
            {
                log.Error($"To undo the steps that did run: moveit rollback --backup \"{backup.Directory}\"");
            }
            return new ProfileResult(source.DisplayName, profile.TargetName, false, backup?.Directory, files, registry, e.Message);
        }
    }

    private Sid PrepareTargetAccount(PlannedProfile profile, BackupSession backup)
    {
        var mapping = profile.Mapping;
        Sid target;

        if (profile.TargetSid is { } existing)
        {
            target = existing;
            if (!string.IsNullOrWhiteSpace(mapping.FullName))
            {
                LocalAccounts.SetFullName(profile.TargetName, mapping.FullName);
            }
        }
        else
        {
            target = LocalAccounts.Create(profile.TargetName, profile.Password!, mapping.FullName, "Created by MoveIT profile migration");
            backup.Manifest.TargetCreated = true;
            backup.Manifest.TargetSid = target.Value;
            backup.MarkStep(MigrationSteps.AccountCreated);
            log.Success($"Created local account '{profile.TargetName}' ({target}).");
        }

        if (mapping.MakeAdmin)
        {
            if (LocalAccounts.AddToAdministrators(target))
            {
                backup.Manifest.TargetAddedToAdministrators = true;
                backup.MarkStep(MigrationSteps.AddedToAdministrators);
                log.Success($"Added '{profile.TargetName}' to the local Administrators group.");
            }
            else
            {
                log.Info($"'{profile.TargetName}' is already a local administrator.");
            }
        }

        if (mapping.MustChangePassword)
        {
            LocalAccounts.RequirePasswordChange(profile.TargetName);
            log.Info($"'{profile.TargetName}' must change their password at next sign-in.");
        }

        return target;
    }

    private AclMigrationStats UpdateHives(string profilePath, Sid source, Sid target, AclMode mode, bool dryRun)
    {
        var total = AclMigrationStats.Empty;
        foreach (var hive in ProfileStore.GetHivePaths(profilePath))
        {
            if (!File.Exists(hive))
            {
                log.Debug($"{hive} not found, skipping.");
                continue;
            }
            total += new RegistryHiveAclMigrator(source, target, mode, dryRun, log).Run(hive);
        }
        return total;
    }

    private void RemoveSourceAccess(PlannedProfile profile, Sid target, BackupSession backup)
    {
        var source = profile.Source;
        log.Info($"Removing the permissions of {source.DisplayName} (aclMode: replace)...");
        backup.MarkStep(MigrationSteps.SourceAclRemovalStarted);
        try
        {
            var files = new FileSystemAclMigrator(source.Sid, target, AclMode.Replace, dryRun: false, log).Run(source.ProfileImagePath);
            var registry = UpdateHives(source.ProfileImagePath, source.Sid, target, AclMode.Replace, dryRun: false);
            backup.MarkStep(MigrationSteps.SourceAclsRemoved);
            log.Info($"Removed old permissions from {files.Changed:N0} files and folders and {registry.Changed:N0} registry keys.");
        }
        catch (Exception e)
        {
            log.Warn($"The profile was migrated, but removing the old account's permissions did not finish: {e.Message}");
        }
    }

    private static void VerifyCriticalFiles(string profilePath, Sid target)
    {
        var critical = new[] { profilePath }
            .Concat(ProfileStore.GetHivePaths(profilePath))
            .Where(path => Directory.Exists(path) || File.Exists(path));

        foreach (var path in critical)
        {
            if (!SecurityDescriptorRewriter.References(FileSecurityApi.ReadDescriptor(path), target))
            {
                throw new MigrationException($"The permissions on {path} were not updated for the new account, so the profile was not reassigned. See the warnings above.");
            }
        }
    }

    private static void EnsureNotLoaded(Sid sid, string displayName)
    {
        if (ProfileStore.IsLoaded(sid))
        {
            throw new MigrationException($"{displayName} signed in during the migration. Sign them out, then run the migration again.");
        }
    }

    private bool ScheduleReboot()
    {
        var result = ProcessRunner.Run(
            Path.Combine(Environment.SystemDirectory, "shutdown.exe"),
            ["/r", "/t", "60", "/c", "MoveIT finished migrating user profiles. The computer will restart in 60 seconds."],
            TimeSpan.FromSeconds(30));

        if (result.ExitCode == 0)
        {
            log.Info("The computer will restart in 60 seconds. Cancel with: shutdown /a");
            return true;
        }

        log.Warn($"Could not schedule a restart: {result.Output.Trim()}");
        return false;
    }
}
