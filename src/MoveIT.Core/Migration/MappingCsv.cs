using MoveIT.Core.Util;

namespace MoveIT.Core.Migration;

/// <summary>
/// Loads a CSV of profile mappings. The only required column is 'source'. A blank 'target' means
/// MoveIT generates the local account name. Columns are matched by name, so their order does not matter.
///   source,target,fullName,createTarget,admin,mustChangePassword,passwordEnv,overwriteTargetProfile
/// </summary>
public static class MappingCsv
{
    private static readonly string[] KnownColumns =
    [
        "source", "target", "fullname", "createtarget", "admin", "makeadmin",
        "mustchangepassword", "passwordenv", "overwritetargetprofile",
    ];

    public static List<ProfileMapping> Load(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ConfigException($"Cannot read mapping file '{path}': {e.Message}");
        }

        return Parse(text);
    }

    public static List<ProfileMapping> Parse(string text)
    {
        var rows = CsvReader.Parse(text);
        if (rows.Count == 0)
        {
            throw new ConfigException("The mapping file is empty.");
        }

        var header = rows[0];
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Length; i++)
        {
            var name = Normalize(header[i]);
            if (name.Length == 0)
            {
                continue;
            }
            if (!KnownColumns.Contains(name))
            {
                throw new ConfigException($"The mapping file has an unknown column '{header[i].Trim()}'. Known columns: {string.Join(", ", KnownColumns.Distinct())}.");
            }
            columns[name] = i;
        }

        if (!columns.ContainsKey("source"))
        {
            throw new ConfigException("The mapping file must have a 'source' column.");
        }

        var mappings = new List<ProfileMapping>();
        for (int r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            var source = Field(row, columns, "source");
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            bool hasTarget = !string.IsNullOrWhiteSpace(Field(row, columns, "target"));
            mappings.Add(new ProfileMapping
            {
                Source = source.Trim(),
                Target = Field(row, columns, "target").Trim(),
                FullName = NullIfEmpty(Field(row, columns, "fullname")),
                // A CSV mapping is normally used to create new accounts; default createTarget to true.
                CreateTarget = Bool(Field(row, columns, "createtarget"), defaultValue: true),
                MakeAdmin = Bool(Field(row, columns, "admin"), Bool(Field(row, columns, "makeadmin"), false)),
                MustChangePassword = Bool(Field(row, columns, "mustchangepassword"), false),
                PasswordEnv = NullIfEmpty(Field(row, columns, "passwordenv")),
                OverwriteTargetProfile = Bool(Field(row, columns, "overwritetargetprofile"), false),
            });

            _ = hasTarget;
        }

        if (mappings.Count == 0)
        {
            throw new ConfigException("The mapping file has a header but no rows.");
        }

        return mappings;
    }

    private static string Field(string[] row, Dictionary<string, int> columns, string name) =>
        columns.TryGetValue(name, out var index) && index < row.Length ? row[index] : "";

    private static string Normalize(string column) =>
        column.Trim().Replace(" ", "").Replace("_", "").ToLowerInvariant();

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool Bool(string value, bool defaultValue) => Normalize(value) switch
    {
        "" => defaultValue,
        "true" or "yes" or "y" or "1" or "x" => true,
        "false" or "no" or "n" or "0" => false,
        var other => throw new ConfigException($"'{other}' is not a yes/no value in the mapping file."),
    };
}
