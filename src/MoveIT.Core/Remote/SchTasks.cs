using System.Globalization;

namespace MoveIT.Core.Remote;

public enum RemoteTaskState
{
    Unknown,
    Running,
    Ready,
    Disabled,
}

/// <summary>Builds schtasks.exe arguments and parses its output. Pure, so it is unit tested off Windows.</summary>
public static class SchTasks
{
    // schtasks reports a "has not run yet" sentinel until the task finishes at least once.
    public const int NotYetRun = 267011;

    public static string[] CreateArgs(string host, string taskName, string runCommand) =>
    [
        "/S", host, "/Create", "/TN", taskName, "/TR", runCommand,
        "/SC", "ONCE", "/ST", "00:00", "/RU", "SYSTEM", "/RL", "HIGHEST", "/F",
    ];

    public static string[] RunArgs(string host, string taskName) =>
        ["/S", host, "/Run", "/TN", taskName];

    public static string[] QueryArgs(string host, string taskName) =>
        ["/S", host, "/Query", "/TN", taskName, "/FO", "LIST", "/V"];

    public static string[] DeleteArgs(string host, string taskName) =>
        ["/S", host, "/Delete", "/TN", taskName, "/F"];

    public static RemoteTaskState ParseState(string queryOutput) => FindValue(queryOutput, "Status") switch
    {
        null => RemoteTaskState.Unknown,
        var value when value.StartsWith("Running", StringComparison.OrdinalIgnoreCase) => RemoteTaskState.Running,
        var value when value.StartsWith("Ready", StringComparison.OrdinalIgnoreCase) => RemoteTaskState.Ready,
        var value when value.StartsWith("Disabled", StringComparison.OrdinalIgnoreCase) => RemoteTaskState.Disabled,
        _ => RemoteTaskState.Unknown,
    };

    /// <summary>The task's last exit code, or null if it has not finished yet.</summary>
    public static int? ParseLastResult(string queryOutput)
    {
        var value = FindValue(queryOutput, "Last Result");
        if (value is null)
        {
            return null;
        }

        int result;
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result))
            {
                return null;
            }
        }
        else if (long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
        {
            result = unchecked((int)parsed);
        }
        else
        {
            return null;
        }

        return result == NotYetRun ? null : result;
    }

    private static string? FindValue(string output, string label)
    {
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            // schtasks /V prints "Label: value" with the label padded; match the start case-insensitively.
            if (line.StartsWith(label, StringComparison.OrdinalIgnoreCase))
            {
                int colon = line.IndexOf(':');
                if (colon >= 0 && line[..colon].Trim().Equals(label, StringComparison.OrdinalIgnoreCase))
                {
                    return line[(colon + 1)..].Trim();
                }
            }
        }
        return null;
    }
}
