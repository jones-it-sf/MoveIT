using System.Runtime.Versioning;
using Microsoft.Win32;
using MoveIT.Core.Security;

namespace MoveIT.Core.Profiles;

public sealed record RegistryValueData(string Name, RegistryValueKind Kind, object Data);

[SupportedOSPlatform("windows")]
public static class ProfileListValues
{
    // Roaming-profile path and domain profile GUID do not apply to a local account.
    private static readonly HashSet<string> DroppedForLocalTarget = new(StringComparer.OrdinalIgnoreCase)
    {
        "Guid",
        "CentralProfile",
    };

    private static readonly HashSet<string> ResetToZero = new(StringComparer.OrdinalIgnoreCase)
    {
        "State",
        "RefCount",
    };

    /// <summary>
    /// Builds the ProfileList values for a local account that takes over an existing profile folder.
    /// </summary>
    public static IReadOnlyList<RegistryValueData> ForLocalTarget(IEnumerable<RegistryValueData> sourceValues, Sid target)
    {
        var result = new List<RegistryValueData>();
        bool hasPath = false, hasState = false, hasFlags = false;

        foreach (var value in sourceValues)
        {
            if (DroppedForLocalTarget.Contains(value.Name) || value.Name.Equals("Sid", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ResetToZero.Contains(value.Name))
            {
                result.Add(value with { Kind = RegistryValueKind.DWord, Data = 0 });
            }
            else
            {
                result.Add(value);
            }

            hasPath |= value.Name.Equals("ProfileImagePath", StringComparison.OrdinalIgnoreCase);
            hasState |= value.Name.Equals("State", StringComparison.OrdinalIgnoreCase);
            hasFlags |= value.Name.Equals("Flags", StringComparison.OrdinalIgnoreCase);
        }

        if (!hasPath)
        {
            throw new InvalidOperationException("The source profile entry has no ProfileImagePath value.");
        }

        if (!hasState)
        {
            result.Add(new RegistryValueData("State", RegistryValueKind.DWord, 0));
        }

        if (!hasFlags)
        {
            result.Add(new RegistryValueData("Flags", RegistryValueKind.DWord, 0));
        }

        result.Add(new RegistryValueData("Sid", RegistryValueKind.Binary, target.ToArray()));
        return result;
    }
}
