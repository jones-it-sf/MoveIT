using System.Diagnostics;

namespace MoveIT.Core.Util;

public sealed record ProcessResult(int ExitCode, string Output);

public static class ProcessRunner
{
    public static ProcessResult Run(
        string fileName,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(timeout))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            throw new TimeoutException($"{Path.GetFileName(fileName)} did not finish within {timeout.TotalSeconds:0} seconds.");
        }

        process.WaitForExit();
        return new ProcessResult(process.ExitCode, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
    }
}
