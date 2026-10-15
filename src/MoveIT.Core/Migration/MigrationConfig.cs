using MoveIT.Core.Accounts;
using MoveIT.Core.Security;

namespace MoveIT.Core.Migration;

public sealed class MigrationConfig
{
    public List<ProfileMapping> Profiles { get; set; } = [];

    public AclMode AclMode { get; set; } = AclMode.Add;

    public bool LeaveEntra { get; set; }

    public bool Reboot { get; set; }

    public string? BackupRoot { get; set; }

    public string? LogDirectory { get; set; }

    /// <summary>Lets the Entra leave step run even when no enabled local administrator would remain.</summary>
    public bool Force { get; set; }

    /// <summary>Discover and migrate every Entra ID profile on the machine, in addition to any listed in Profiles.</summary>
    public bool MigrateAllEntraProfiles { get; set; }

    /// <summary>Sources to skip during discovery: SID, account name, Entra object ID or profile path.</summary>
    public List<string> ExcludeSources { get; set; } = [];

    /// <summary>How to invent a local account name when a mapping leaves 'target' blank.</summary>
    public NamingOptions Naming { get; set; } = new();

    /// <summary>Create new accounts with a strong random password and write them to a credentials file.</summary>
    public bool GeneratePasswords { get; set; }

    public int GeneratedPasswordLength { get; set; } = 20;

    /// <summary>Where to write generated passwords. Defaults to a secured file under the backup root.</summary>
    public string? CredentialsOutputPath { get; set; }

    /// <summary>Scripts MoveIT runs during the migration. They run in the elevated/SYSTEM context of MoveIT.</summary>
    public ScriptHooks Scripts { get; set; } = new();

    /// <summary>Validates the configuration as written. Blank targets are allowed only when they will be generated.</summary>
    public IReadOnlyList<string> Validate(string machineName)
    {
        var errors = new List<string>();
        if (Profiles.Count == 0 && !MigrateAllEntraProfiles)
        {
            errors.Add("At least one profile mapping is required (or enable migrateAllEntraProfiles).");
        }

        if (GeneratedPasswordLength is < 12 or > 127)
        {
            errors.Add("generatedPasswordLength must be between 12 and 127.");
        }

        errors.AddRange(ValidateMappings(Profiles, machineName, targetsMayBeBlank: true));
        return errors;
    }

    /// <summary>
    /// Validates a resolved mapping list, after any blank targets have been filled in. Used by the engine
    /// on the effective set of mappings (explicit + CSV + discovered).
    /// </summary>
    public static IReadOnlyList<string> ValidateMappings(IReadOnlyList<ProfileMapping> mappings, string machineName, bool targetsMayBeBlank)
    {
        var errors = new List<string>();
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < mappings.Count; i++)
        {
            var mapping = mappings[i];
            var label = $"profiles[{i}]";

            if (string.IsNullOrWhiteSpace(mapping.Source))
            {
                errors.Add($"{label}: 'source' is required.");
            }
            else if (!sources.Add(mapping.Source.Trim()))
            {
                errors.Add($"{label}: source '{mapping.Source}' appears more than once.");
            }

            if (string.IsNullOrWhiteSpace(mapping.Target))
            {
                if (!targetsMayBeBlank)
                {
                    errors.Add($"{label}: 'target' is required.");
                }
            }
            else if (!LocalAccountName.TryNormalize(mapping.Target, machineName, out var targetName, out var nameError))
            {
                errors.Add($"{label}: {nameError}");
            }
            else if (!targets.Add(targetName))
            {
                errors.Add($"{label}: target '{targetName}' appears more than once.");
            }

            if (mapping.Password is not null && mapping.PasswordEnv is not null)
            {
                errors.Add($"{label}: set either 'password' or 'passwordEnv', not both.");
            }

            if ((mapping.Password is not null || mapping.PasswordEnv is not null) && !mapping.CreateTarget)
            {
                errors.Add($"{label}: a password is only used when 'createTarget' is true.");
            }

            if (mapping.DisableSource && mapping.DeleteSource)
            {
                errors.Add($"{label}: set either 'disableSource' or 'deleteSource', not both.");
            }
        }

        return errors;
    }
}

public sealed class ProfileMapping
{
    /// <summary>SID, Entra object ID, profile path, or account name such as "AzureAD\JohnSmith".</summary>
    public string Source { get; set; } = "";

    /// <summary>Local account name: "jsmith", ".\jsmith" or "COMPUTER\jsmith".</summary>
    public string Target { get; set; } = "";

    public bool CreateTarget { get; set; }

    public string? FullName { get; set; }

    public string? Password { get; set; }

    public string? PasswordEnv { get; set; }

    public bool MakeAdmin { get; set; }

    public bool MustChangePassword { get; set; }

    /// <summary>Replace the target's existing profile registration. Its old folder stays on disk.</summary>
    public bool OverwriteTargetProfile { get; set; }

    /// <summary>After a successful migration, disable the source account (only if it is a local account).</summary>
    public bool DisableSource { get; set; }

    /// <summary>After a successful migration, delete the source account (only if it is a local account). Cannot be rolled back.</summary>
    public bool DeleteSource { get; set; }
}

public sealed class ScriptHooks
{
    /// <summary>Run after each profile is migrated. Receives MOVEIT_* environment variables for that profile.</summary>
    public string? AfterEach { get; set; }

    /// <summary>Run once after all profiles are migrated and before leaving Entra ID.</summary>
    public string? AfterAll { get; set; }

    public int TimeoutMinutes { get; set; } = 10;
}
