using MoveIT.Core.Acl;
using MoveIT.Core.Entra;
using MoveIT.Core.Profiles;
using MoveIT.Core.Security;

namespace MoveIT.Core.Migration;

/// <summary>A profile mapping that passed the pre-flight checks. Not a record, so the password never appears in ToString.</summary>
public sealed class PlannedProfile(ProfileMapping mapping, ProfileEntry source, string targetName, Sid? targetSid, ProfileEntry? targetExistingProfile, string? password)
{
    public ProfileMapping Mapping { get; } = mapping;

    public ProfileEntry Source { get; } = source;

    public string TargetName { get; } = targetName;

    /// <summary>Null when the account will be created.</summary>
    public Sid? TargetSid { get; } = targetSid;

    public ProfileEntry? TargetExistingProfile { get; } = targetExistingProfile;

    internal string? Password { get; } = password;

    public bool CreatesTarget => TargetSid is null;
}

public sealed record MigrationPlan(
    IReadOnlyList<PlannedProfile> Profiles,
    EntraJoinStatus? JoinStatus,
    bool LeaveEntra,
    IReadOnlyList<string> Warnings);

public sealed record ProfileResult(
    string Source,
    string Target,
    bool Succeeded,
    string? BackupDirectory,
    AclMigrationStats Files,
    AclMigrationStats Registry,
    string? Error);

public sealed record MigrationReport(
    IReadOnlyList<ProfileResult> Profiles,
    bool LeftEntra,
    bool EntraLeaveFailed,
    bool RebootScheduled)
{
    public bool Succeeded => Profiles.All(p => p.Succeeded) && !EntraLeaveFailed;
}
