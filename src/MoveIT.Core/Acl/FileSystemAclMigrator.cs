using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Enumeration;
using System.Runtime.Versioning;
using MoveIT.Core.Logging;
using MoveIT.Core.Security;
using static MoveIT.Core.Native.NativeMethods;

namespace MoveIT.Core.Acl;

/// <summary>
/// Walks a profile folder and rewrites the security descriptor of every file and folder that
/// references the source account. Junctions and symbolic links are updated but not followed.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FileSystemAclMigrator(Sid source, Sid target, AclMode mode, bool dryRun, Logger log)
{
    private static readonly EnumerationOptions ChildrenOnly = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    private long _scanned;
    private long _changed;
    private long _skippedLinks;

    public AclMigrationStats Run(string root)
    {
        root = Path.GetFullPath(root);
        var stopwatch = Stopwatch.StartNew();
        var failures = new FailureRecorder(log, "Files");

        using var progress = new Timer(
            _ => log.Info($"  ...{Interlocked.Read(ref _scanned):N0} items scanned, {Interlocked.Read(ref _changed):N0} updated"),
            null,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(15));

        ProcessObject(root, File.GetAttributes(root), failures);

        var parallelism = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8) };
        var level = new List<string> { root };
        while (level.Count > 0)
        {
            var next = new ConcurrentBag<string>();
            Parallel.ForEach(level, parallelism, directory => ProcessChildren(directory, next, failures));
            level = [.. next];
        }

        failures.Summarize();
        return new AclMigrationStats(_scanned, _changed, failures.Count, _skippedLinks, stopwatch.Elapsed);
    }

    private void ProcessChildren(string directory, ConcurrentBag<string> subdirectories, FailureRecorder failures)
    {
        List<(string Path, FileAttributes Attributes)> entries;
        try
        {
            entries = new FileSystemEnumerable<(string, FileAttributes)>(
                directory,
                static (ref FileSystemEntry entry) => (entry.ToFullPath(), entry.Attributes),
                ChildrenOnly).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            failures.Record(directory, $"cannot list folder: {e.Message}");
            return;
        }

        foreach (var (path, attributes) in entries)
        {
            if (ProcessObject(path, attributes, failures))
            {
                subdirectories.Add(path);
            }
        }
    }

    /// <summary>Updates one object and returns true if the walk should descend into it.</summary>
    private bool ProcessObject(string path, FileAttributes attributes, FailureRecorder failures)
    {
        Interlocked.Increment(ref _scanned);
        bool isDirectory = attributes.HasFlag(FileAttributes.Directory);
        bool isReparsePoint = attributes.HasFlag(FileAttributes.ReparsePoint);

        using var handle = FileSecurityApi.Open(path, forWrite: !dryRun, out var openError);
        if (handle.IsInvalid)
        {
            failures.Record(path, new Win32Exception(openError).Message);
            return isDirectory && !isReparsePoint;
        }

        try
        {
            var change = SecurityDescriptorRewriter.Rewrite(FileSecurityApi.GetSecurity(handle), source, target, mode);
            if (change is not null)
            {
                if (!dryRun)
                {
                    FileSecurityApi.SetSecurity(handle, change);
                }
                Interlocked.Increment(ref _changed);
            }
        }
        catch (Exception e) when (e is Win32Exception or FormatException or InvalidOperationException)
        {
            failures.Record(path, e.Message);
        }

        if (!isDirectory)
        {
            return false;
        }

        if (!isReparsePoint)
        {
            return true;
        }

        // Junctions and symlinks point elsewhere; OneDrive placeholders are reparse points we must enter.
        uint? tag = FileSecurityApi.GetReparseTag(handle);
        if (tag is null || (tag.Value & IO_REPARSE_TAG_NAME_SURROGATE) != 0)
        {
            Interlocked.Increment(ref _skippedLinks);
            return false;
        }
        return true;
    }
}
