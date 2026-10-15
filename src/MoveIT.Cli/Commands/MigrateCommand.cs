using System.Runtime.Versioning;
using MoveIT.Core;
using MoveIT.Core.Logging;
using MoveIT.Core.Migration;
using MoveIT.Core.Security;

namespace MoveIT.Cli.Commands;

[SupportedOSPlatform("windows")]
internal static class MigrateCommand
{
    private static readonly HashSet<string> AllowedWithConfig = new(StringComparer.OrdinalIgnoreCase)
    {
        "config", "what-if", "yes", "verbose", "log",
    };

    public static int Run(ParsedCommand command)
    {
        MigrationConfig config;
        try
        {
            config = BuildConfig(command);
        }
        catch (ConfigException e)
        {
            Console.Error.WriteLine(e.Message);
            return ExitCodes.Usage;
        }

        bool whatIf = command.Has("what-if");
        using var log = LogSetup.Create(command.Get("log"), config.LogDirectory, "migrate", command.Has("verbose"));
        log.Info($"MoveIT {ToolInfo.Version} on {Environment.MachineName}{(whatIf ? " (what-if: nothing will be changed)" : "")}");
        log.Info($"Log file: {log.FilePath}");

        var engine = new MigrationEngine(config, log, ConsoleInput.IsInteractive ? ConsoleInput.ReadNewPassword : null);

        MigrationPlan plan;
        try
        {
            plan = engine.Prepare(whatIf);
        }
        catch (Exception e) when (e is MigrationException or UnauthorizedAccessException)
        {
            log.Error(e.Message);
            if (e is MigrationException migrationException)
            {
                foreach (var detail in migrationException.Details)
                {
                    log.Error("  - " + detail);
                }
            }
            log.Error("Nothing was changed.");
            return ExitCodes.NothingChanged;
        }

        PrintPlan(plan, config, log);

        if (!whatIf && !command.Has("yes"))
        {
            if (!ConsoleInput.IsInteractive)
            {
                log.Error("This session is not interactive. Add --yes to confirm the migration. Nothing was changed.");
                return ExitCodes.NothingChanged;
            }

            if (!ConsoleInput.Confirm("Start the migration?"))
            {
                log.Warn("Cancelled. Nothing was changed.");
                return ExitCodes.NothingChanged;
            }
        }

        var report = engine.Execute(plan, whatIf);
        PrintSummary(report, whatIf, log);

        if (!report.Succeeded)
        {
            return ExitCodes.Failed;
        }
        return log.WarningCount > 0 ? ExitCodes.CompletedWithWarnings : ExitCodes.Success;
    }

    internal static MigrationConfig BuildConfig(ParsedCommand command)
    {
        if (command.Get("config") is { } configPath)
        {
            var conflicting = command.OptionNames.FirstOrDefault(option => !AllowedWithConfig.Contains(option));
            if (conflicting is not null)
            {
                throw new CommandLineException($"--{conflicting} cannot be combined with --config. Put that setting in the config file instead.");
            }
            return MigrationConfigLoader.Load(configPath);
        }

        var aclMode = command.Get("acl-mode")?.ToLowerInvariant() switch
        {
            null or "add" => AclMode.Add,
            "replace" => AclMode.Replace,
            var other => throw new CommandLineException($"--acl-mode must be 'add' or 'replace', not '{other}'."),
        };

        bool migrateAll = command.Has("all");
        var mapFile = command.Get("map");
        var profiles = new List<ProfileMapping>();

        if (mapFile is not null)
        {
            profiles.AddRange(MappingCsv.Load(mapFile));
        }

        if (command.Get("source") is { } source)
        {
            profiles.Add(new ProfileMapping
            {
                Source = source,
                Target = command.Get("target") ?? "",
                CreateTarget = command.Has("create"),
                FullName = command.Get("full-name"),
                PasswordEnv = command.Get("password-env"),
                MakeAdmin = command.Has("admin"),
                MustChangePassword = command.Has("must-change-password"),
                OverwriteTargetProfile = command.Has("overwrite-target-profile"),
                DisableSource = command.Has("disable-source"),
                DeleteSource = command.Has("delete-source"),
            });
        }
        else if (command.Has("target"))
        {
            throw new CommandLineException("--target was given without --source.");
        }

        if (profiles.Count == 0 && !migrateAll)
        {
            throw new CommandLineException("Specify what to migrate with --source, --map <file.csv>, --all, or --config.");
        }

        return new MigrationConfig
        {
            Profiles = profiles,
            AclMode = aclMode,
            MigrateAllEntraProfiles = migrateAll,
            ExcludeSources = SplitList(command.Get("exclude")),
            Naming = new NamingOptions { Prefix = command.Get("name-prefix") ?? "" },
            GeneratePasswords = command.Has("generate-passwords"),
            CredentialsOutputPath = command.Get("credentials-out"),
            Scripts = new ScriptHooks
            {
                AfterEach = command.Get("after-each"),
                AfterAll = command.Get("after-all"),
            },
            LeaveEntra = command.Has("leave-entra"),
            Reboot = command.Has("reboot"),
            BackupRoot = command.Get("backup-root"),
            Force = command.Has("force"),
        };
    }

    private static List<string> SplitList(string? value) =>
        value is null ? [] : [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static void PrintPlan(MigrationPlan plan, MigrationConfig config, Logger log)
    {
        log.Info("");
        log.Info("Plan:");
        foreach (var profile in plan.Profiles)
        {
            var source = profile.Source;
            var mapping = profile.Mapping;
            log.Info($"  {source.DisplayName} ({source.Kind}, {source.Sid})");
            log.Info($"    profile folder : {source.ProfileImagePath}");
            log.Info($"    new owner      : {Environment.MachineName}\\{profile.TargetName} ({(profile.CreatesTarget ? "will be created" : $"existing, {profile.TargetSid}")})");

            var extras = new List<string>();
            if (mapping.MakeAdmin)
            {
                extras.Add("administrator");
            }
            if (mapping.MustChangePassword)
            {
                extras.Add("must change password");
            }
            if (profile.TargetExistingProfile is not null)
            {
                extras.Add($"replaces profile at {profile.TargetExistingProfile.ProfileImagePath}");
            }
            if (extras.Count > 0)
            {
                log.Info($"    options        : {string.Join(", ", extras)}");
            }
        }

        log.Info($"  Permissions    : {(config.AclMode == AclMode.Add ? "add (the old account keeps access)" : "replace (the old account's access is removed afterwards)")}");
        if (config.LeaveEntra)
        {
            var tenant = plan.JoinStatus?.TenantName is { } name ? $" ({name})" : "";
            log.Info($"  Leave Entra ID : {(plan.LeaveEntra ? $"yes{tenant}, after all profiles succeed" : "no, the device is not Entra joined")}");
        }
        if (config.Reboot)
        {
            log.Info("  Restart        : yes, 60 seconds after success");
        }

        foreach (var warning in plan.Warnings)
        {
            log.Warn(warning);
        }
        log.Info("");
    }

    private static void PrintSummary(MigrationReport report, bool whatIf, Logger log)
    {
        log.Info("");
        log.Info(whatIf ? "What-if summary:" : "Summary:");
        foreach (var result in report.Profiles)
        {
            var status = result.Succeeded ? (whatIf ? "ready" : "migrated") : "FAILED";
            log.Write(result.Succeeded ? LogLevel.Info : LogLevel.Error, $"  {result.Source} -> {result.Target}: {status}{(result.Error is null ? "" : $" ({result.Error})")}");
            if (result.BackupDirectory is not null)
            {
                log.Info($"    backup: {result.BackupDirectory}");
            }
        }

        if (report.LeftEntra)
        {
            log.Info("  The device has left Entra ID. Restart it before users sign in.");
        }

        if (!whatIf && report.Succeeded)
        {
            log.Info("  Users can now sign in with their local account and will see their existing desktop, files and settings.");
        }
    }
}
