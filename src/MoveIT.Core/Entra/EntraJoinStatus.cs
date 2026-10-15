namespace MoveIT.Core.Entra;

public sealed record EntraJoinStatus(
    bool AzureAdJoined,
    bool DomainJoined,
    bool EnterpriseJoined,
    string? TenantName,
    string? TenantId,
    string? DeviceId)
{
    /// <summary>Parses the output of "dsregcmd /status".</summary>
    public static EntraJoinStatus Parse(string output)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            int separator = line.IndexOf(" : ", StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }
            values.TryAdd(line[..separator].Trim(), line[(separator + 3)..].Trim());
        }

        return new EntraJoinStatus(
            IsYes("AzureAdJoined"),
            IsYes("DomainJoined"),
            IsYes("EnterpriseJoined"),
            Get("TenantName"),
            Get("TenantId"),
            Get("DeviceId"));

        bool IsYes(string key) => values.TryGetValue(key, out var value) && value.Equals("YES", StringComparison.OrdinalIgnoreCase);

        string? Get(string key) => values.TryGetValue(key, out var value) && value.Length > 0 ? value : null;
    }
}
