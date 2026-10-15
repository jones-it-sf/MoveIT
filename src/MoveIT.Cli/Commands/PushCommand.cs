using System.Reflection;
using System.Runtime.Versioning;
using MoveIT.Core;
using MoveIT.Core.Remote;
using MoveIT.Core.Util;

namespace MoveIT.Cli.Commands;

[SupportedOSPlatform("windows")]
internal static class PushCommand
{
    public static int Run(ParsedCommand command)
    {
        var computers = ResolveComputers(command);
        if (computers.Count == 0)
        {
            throw new CommandLineException("No computers to push to. Use --computers <file> or --computer <name>.");
        }

        var configPath = command.Get("config");
        var verbatim = command.Get("args") is { } raw
            ? raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : null;

        if (configPath is null && verbatim is null)
        {
            throw new CommandLineException("Specify what to run on each machine with --config <file.json> or --args \"migrate --all ...\".");
        }
        if (configPath is not null && verbatim is not null)
        {
            throw new CommandLineException("Use either --config or --args, not both.");
        }
        if (configPath is not null && !File.Exists(configPath))
        {
            throw new CommandLineException($"Config file '{configPath}' does not exist.");
        }

        var exePath = Environment.ProcessPath
            ?? throw new CommandLineException("Could not determine the path to moveit.exe.");

        var resultsDir = command.Get("results-dir")
            ?? Path.Combine(AppDirectories.Ensure(Path.Combine(AppDirectories.Root, "PushResults")), DateTime.Now.ToString("yyyyMMdd-HHmmss"));

        using var log = LogSetup.Create(command.Get("log"), null, "push", command.Has("verbose"));
        log.Info($"MoveIT {ToolInfo.Version} push to {computers.Count} computer(s) on {Environment.MachineName}");
        log.Info($"Results and logs: {resultsDir}");

        if (!command.Has("yes"))
        {
            if (!ConsoleInput.IsInteractive)
            {
                log.Error("This session is not interactive. Add --yes to confirm. Nothing was changed.");
                return ExitCodes.NothingChanged;
            }
            if (!ConsoleInput.Confirm($"Push a migration to {computers.Count} computer(s)?"))
            {
                log.Warn("Cancelled. Nothing was changed.");
                return ExitCodes.NothingChanged;
            }
        }

        var wrapperPath = ExtractWrapper();
        try
        {
            var options = new PushOptions
            {
                Computers = computers,
                MoveItExePath = exePath,
                WrapperScriptPath = wrapperPath,
                ConfigPath = configPath,
                VerbatimArgs = verbatim,
                ResultsDirectory = resultsDir,
                Parallelism = ParseParallel(command.Get("parallel")),
                RemoteStageDirectory = command.Get("stage") ?? @"C:\Windows\Temp\MoveIT",
                TaskName = command.Get("task-name") ?? "MoveIT-Migration",
            };

            var results = new PushOrchestrator(options, log).Run();

            int ok = results.Count(r => r.Succeeded);
            log.Info("");
            log.Info($"Done. {ok} of {results.Count} succeeded. Per-machine logs are under {resultsDir}.");
            return ok == results.Count ? ExitCodes.Success : ExitCodes.Failed;
        }
        finally
        {
            File.Delete(wrapperPath);
        }
    }

    private static List<string> ResolveComputers(ParsedCommand command)
    {
        if (command.Get("computers") is { } file)
        {
            if (!File.Exists(file))
            {
                throw new CommandLineException($"Computers file '{file}' does not exist.");
            }
            return RemoteHosts.Load(file);
        }

        return command.Get("computer") is { } single ? RemoteHosts.Parse(single) : [];
    }

    private static int ParseParallel(string? value)
    {
        if (value is null)
        {
            return 10;
        }
        if (!int.TryParse(value, out var parallel) || parallel < 1)
        {
            throw new CommandLineException("--parallel must be a positive whole number.");
        }
        return parallel;
    }

    private static string ExtractWrapper()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("moveit.wrapper.ps1")
            ?? throw new InvalidOperationException("The embedded deployment wrapper is missing from this build.");
        var path = Path.Combine(Path.GetTempPath(), $"Invoke-MoveItMigration-{Guid.NewGuid():N}.ps1");
        using var file = File.Create(path);
        stream.CopyTo(file);
        return path;
    }
}
