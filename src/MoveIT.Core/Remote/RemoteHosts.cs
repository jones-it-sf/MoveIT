namespace MoveIT.Core.Remote;

public static class RemoteHosts
{
    private static readonly char[] Separators = [',', ';', ' ', '\t', '\r', '\n'];

    /// <summary>Parses a hosts list: one or more names per line, '#' starts a comment, duplicates removed.</summary>
    public static List<string> Parse(string text)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine;
            int comment = line.IndexOf('#');
            if (comment >= 0)
            {
                line = line[..comment];
            }

            foreach (var token in line.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var host = token.Trim('\\', '/');
                if (host.Length > 0 && seen.Add(host))
                {
                    result.Add(host);
                }
            }
        }

        return result;
    }

    public static List<string> Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Builds a path on a remote machine's admin share: \\host\C$\Windows\Temp\MoveIT.</summary>
    public static string AdminShare(string host, string localPath)
    {
        if (localPath.Length < 2 || localPath[1] != ':')
        {
            throw new ArgumentException($"'{localPath}' is not a local drive path.", nameof(localPath));
        }

        var drive = char.ToUpperInvariant(localPath[0]);
        var rest = localPath[2..].TrimStart('\\', '/');
        return $@"\\{host}\{drive}$\{rest}";
    }
}
