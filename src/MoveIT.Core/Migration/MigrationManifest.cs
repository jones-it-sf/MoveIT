using MoveIT.Core.Security;

namespace MoveIT.Core.Migration;

/// <summary>Written to the backup folder as each step finishes, so a rollback knows what to undo.</summary>
public sealed class MigrationManifest
{
    public int FormatVersion { get; set; } = 1;

    public string ToolVersion { get; set; } = "";

    public string ComputerName { get; set; } = "";

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string SourceSid { get; set; } = "";

    public string? SourceAccount { get; set; }

    public string? SourceProfileGuid { get; set; }

    public string ProfilePath { get; set; } = "";

    public string TargetAccount { get; set; } = "";

    public string? TargetSid { get; set; }

    public bool TargetCreated { get; set; }

    public bool TargetAddedToAdministrators { get; set; }

    public bool TargetProfileEntryReplaced { get; set; }

    public AclMode AclMode { get; set; }

    public List<string> CompletedSteps { get; set; } = [];

    public bool RolledBack { get; set; }

    public bool HasStep(string step) => CompletedSteps.Contains(step, StringComparer.Ordinal);
}

public static class MigrationSteps
{
    public const string AccountCreated = "AccountCreated";
    public const string AddedToAdministrators = "AddedToAdministrators";
    public const string RegistryBackedUp = "RegistryBackedUp";
    public const string AclUpdateStarted = "AclUpdateStarted";
    public const string FileAclsUpdated = "FileAclsUpdated";
    public const string HiveAclsUpdated = "HiveAclsUpdated";
    public const string ProfileListReassigned = "ProfileListReassigned";
    public const string SourceAclRemovalStarted = "SourceAclRemovalStarted";
    public const string SourceAclsRemoved = "SourceAclsRemoved";
}
