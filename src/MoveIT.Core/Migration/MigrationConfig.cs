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

    public IReadOnlyList<string> Validate(string machineName)
    {
        var errors = new List<string>();
        if (Profiles.Count == 0)
        {
            errors.Add("At least one profile mapping is required.");
        }

        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < Profiles.Count; i++)
        {
            var mapping = Profiles[i];
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
                errors.Add($"{label}: 'target' is required.");
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
}
