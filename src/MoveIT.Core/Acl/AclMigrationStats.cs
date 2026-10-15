namespace MoveIT.Core.Acl;

public sealed record AclMigrationStats(long Scanned, long Changed, long Failed, long SkippedLinks, TimeSpan Elapsed)
{
    public static AclMigrationStats Empty { get; } = new(0, 0, 0, 0, TimeSpan.Zero);

    public static AclMigrationStats operator +(AclMigrationStats left, AclMigrationStats right) => new(
        left.Scanned + right.Scanned,
        left.Changed + right.Changed,
        left.Failed + right.Failed,
        left.SkippedLinks + right.SkippedLinks,
        left.Elapsed + right.Elapsed);
}
