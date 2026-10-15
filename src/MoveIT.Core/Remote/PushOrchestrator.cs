using System.Collections.Concurrent;
using System.Runtime.Versioning;
using MoveIT.Core.Logging;
using MoveIT.Core.Util;

namespace MoveIT.Core.Remote;

public sealed class PushOptions
{
    public required IReadOnlyList<string> Computers { get; init; }

    public required string MoveItExePath { get; init; }

    public required string WrapperScriptPath { get; init; }

    public string? ConfigPath { get; init; }

    public IReadOnlyList<string>? VerbatimArgs { get; init; }

    public int Parallelism { get; init; } = 10;

    public required string ResultsDirectory { get; init; }

    /// <summary>The staging folder on each remote machine (a local path there, not a UNC path).</summary>
    public string RemoteStageDirectory { get; init; } = @"C:\Windows\Temp\MoveIT";

    public string TaskName { get; init; } = "MoveIT-Migration";

    public TimeSpan PerHostTimeout { get; init; } = TimeSpan.FromHours(2);
}

public sealed record PushResult(string Host, bool Succeeded, int? ExitCode, string Message, string? LogDirectory);

/// <summary>
/// Pushes a migration to remote machines using only built-in Windows plumbing: it copies MoveIT to
/// each machine's admin share, then creates and runs a one-off scheduled task as SYSTEM, polls it to
/// completion, and copies the logs back. It uses the caller's credentials, so run it as a domain or
/// local admin that the targets trust.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PushOrchestrator(PushOptions options, Logger log)
{
    public IReadOnlyList<PushResult> Run()
    {
        if (!Environment.IsPrivilegedProcess)
        {
            throw new InvalidOperationException("Run 'moveit push' from an elevated prompt as an administrator the target machines trust.");
        }

        Directory.CreateDirectory(options.ResultsDirectory);
        var results = new ConcurrentBag<PushResult>();
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(options.Parallelism, 1, 64) };

        Parallel.ForEach(options.Computers, parallel, host =>
        {
            var result = RunHost(host);
            results.Add(result);
            var line = $"{host}: {(result.Succeeded ? "ok" : "FAILED")} - {result.Message}";
            log.Write(result.Succeeded ? LogLevel.Success : LogLevel.Error, line);
        });

        return [.. results.OrderBy(r => r.Host, StringComparer.OrdinalIgnoreCase)];
    }

    private PushResult RunHost(string host)
    {
        var remoteStage = RemoteHosts.AdminShare(host, options.RemoteStageDirectory);
        var localResults = Path.Combine(options.ResultsDirectory, SafeName(host));

        try
        {
            StageFiles(remoteStage);

            Run("schtasks.exe", SchTasks.CreateArgs(host, options.TaskName, Path.Combine(options.RemoteStageDirectory, "run.cmd")), $"create task on {host}");
            Run("schtasks.exe", SchTasks.RunArgs(host, options.TaskName), $"start task on {host}");

            var exitCode = WaitForCompletion(host);
            CollectLogs(host, localResults);

            bool ok = exitCode is 0 or 3010;
            var message = exitCode switch
            {
                null => "timed out waiting for the remote task to finish",
                0 => "migration succeeded",
                3010 => "migration succeeded; the machine needs a reboot",
                _ => $"migration failed (exit code {exitCode})",
            };
            return new PushResult(host, ok, exitCode, message, Directory.Exists(localResults) ? localResults : null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or TimeoutException or InvalidOperationException)
        {
            return new PushResult(host, false, null, e.Message, Directory.Exists(localResults) ? localResults : null);
        }
        finally
        {
            TryCleanup(host, remoteStage);
        }
    }

    private void StageFiles(string remoteStage)
    {
        Directory.CreateDirectory(remoteStage);
        File.Copy(options.MoveItExePath, Path.Combine(remoteStage, "moveit.exe"), overwrite: true);
        File.Copy(options.WrapperScriptPath, Path.Combine(remoteStage, "Invoke-MoveItMigration.ps1"), overwrite: true);

        string wrapperArgs;
        if (options.ConfigPath is not null)
        {
            File.Copy(options.ConfigPath, Path.Combine(remoteStage, "moveit.json"), overwrite: true);
            wrapperArgs = $"-ConfigPath \"{options.RemoteStageDirectory}\\moveit.json\"";
        }
        else
        {
            var tokens = string.Join(",", (options.VerbatimArgs ?? ["migrate", "--all"]).Select(a => $"'{a.Replace("'", "''")}'"));
            wrapperArgs = $"-ArgumentList {tokens}";
        }

        var wrapperPath = $"{options.RemoteStageDirectory}\\Invoke-MoveItMigration.ps1";
        var runCmd = $"@echo off\r\npowershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{wrapperPath}\" {wrapperArgs}\r\n";
        File.WriteAllText(Path.Combine(remoteStage, "run.cmd"), runCmd);
    }

    private int? WaitForCompletion(string host)
    {
        var deadline = DateTime.UtcNow + options.PerHostTimeout;
        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(TimeSpan.FromSeconds(10));
            var query = ProcessRunner.Run("schtasks.exe", SchTasks.QueryArgs(host, options.TaskName), TimeSpan.FromMinutes(1));
            if (query.ExitCode != 0)
            {
                continue;
            }

            if (SchTasks.ParseState(query.Output) != RemoteTaskState.Running && SchTasks.ParseLastResult(query.Output) is { } result)
            {
                return result;
            }
        }
        return null;
    }

    private void CollectLogs(string host, string localResults)
    {
        var remoteLogs = RemoteHosts.AdminShare(host, @"C:\ProgramData\MoveIT\Logs");
        if (!Directory.Exists(remoteLogs))
        {
            return;
        }

        Directory.CreateDirectory(localResults);
        foreach (var file in Directory.EnumerateFiles(remoteLogs, "*.log"))
        {
            try
            {
                File.Copy(file, Path.Combine(localResults, Path.GetFileName(file)), overwrite: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private void TryCleanup(string host, string remoteStage)
    {
        try
        {
            ProcessRunner.Run("schtasks.exe", SchTasks.DeleteArgs(host, options.TaskName), TimeSpan.FromMinutes(1));
        }
        catch (Exception e) when (e is IOException or TimeoutException or InvalidOperationException)
        {
            log.Debug($"{host}: could not delete the scheduled task: {e.Message}");
        }

        try
        {
            if (Directory.Exists(remoteStage))
            {
                Directory.Delete(remoteStage, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Debug($"{host}: could not remove the staging folder {remoteStage}: {e.Message}");
        }
    }

    private void Run(string fileName, string[] arguments, string what)
    {
        var result = ProcessRunner.Run(fileName, arguments, TimeSpan.FromMinutes(2));
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Could not {what}: {result.Output.Trim()}");
        }
    }

    private static string SafeName(string host) =>
        string.Concat(host.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
