using MoveIT.Core.Security;

namespace MoveIT.Core.Profiles;

/// <summary>One entry under HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList.</summary>
public sealed record ProfileEntry(
    Sid Sid,
    string ProfileImagePath,
    string? AccountName,
    SidKind Kind,
    bool IsLoaded,
    bool IsBackupEntry,
    Guid? ProfileGuid,
    DateTime? LastUsed)
{
    public string DisplayName => AccountName ?? Sid.Value;
}
