using System.Runtime.Versioning;
using MoveIT.Core.Profiles;

namespace MoveIT.Cli.Commands;

[SupportedOSPlatform("windows")]
internal static class ListCommand
{
    public static int Run(ParsedCommand command)
    {
        var profiles = ProfileStore.GetProfiles(includeSystem: command.Has("all"));
        if (profiles.Count == 0)
        {
            Console.WriteLine("No user profiles found.");
            return ExitCodes.Success;
        }

        var rows = profiles.Select(p => new[]
        {
            p.Kind.ToString() + (p.IsBackupEntry ? " (.bak)" : ""),
            p.AccountName ?? "(unresolved)",
            p.IsLoaded ? "yes" : "no",
            p.LastUsed?.ToString("yyyy-MM-dd") ?? "",
            p.ProfileImagePath,
            p.Sid.Value,
        }).ToList();

        string[] headers = ["TYPE", "ACCOUNT", "LOADED", "LAST USED", "PATH", "SID"];
        var widths = headers.Select((header, column) => Math.Max(header.Length, rows.Max(row => row[column].Length))).ToArray();

        Console.WriteLine(Format(headers, widths));
        foreach (var row in rows)
        {
            Console.WriteLine(Format(row, widths));
        }

        if (profiles.Any(p => p.IsBackupEntry))
        {
            Console.WriteLine();
            Console.WriteLine("Profiles marked (.bak) have a duplicate registry entry; Windows usually signs those users in with a temporary profile.");
        }

        return ExitCodes.Success;
    }

    private static string Format(string[] cells, int[] widths) =>
        string.Join("  ", cells.Select((cell, i) => i == cells.Length - 1 ? cell : cell.PadRight(widths[i])));
}
