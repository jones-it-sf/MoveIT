using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MoveIT.Core.Security;
using static MoveIT.Core.Native.NativeMethods;

namespace MoveIT.Core.Accounts;

[SupportedOSPlatform("windows")]
public static class AccountResolver
{
    private static readonly Lazy<Sid?> MachineSid = new(LookupMachineSid);

    /// <summary>Returns "DOMAIN\name" (for example "AzureAD\JohnSmith"), or null if Windows cannot resolve the SID.</summary>
    public static string? TryGetAccountName(Sid sid)
    {
        var binary = sid.ToArray();
        uint nameLength = 0, domainLength = 0;
        LookupAccountSid(null, binary, null, ref nameLength, null, ref domainLength, out _);
        if (Marshal.GetLastPInvokeError() != ERROR_INSUFFICIENT_BUFFER)
        {
            return null;
        }

        var name = new char[nameLength];
        var domain = new char[domainLength];
        if (!LookupAccountSid(null, binary, name, ref nameLength, domain, ref domainLength, out _))
        {
            return null;
        }

        var account = new string(name, 0, (int)nameLength);
        var domainName = new string(domain, 0, (int)domainLength);
        return domainName.Length > 0 ? $"{domainName}\\{account}" : account;
    }

    public static bool TryLookupName(string accountName, [NotNullWhen(true)] out Sid? sid, out int sidUse)
    {
        sid = null;
        sidUse = 0;
        uint sidLength = 0, domainLength = 0;
        LookupAccountName(null, accountName, null, ref sidLength, null, ref domainLength, out _);
        if (Marshal.GetLastPInvokeError() != ERROR_INSUFFICIENT_BUFFER)
        {
            return false;
        }

        var sidBuffer = new byte[sidLength];
        var domain = new char[domainLength];
        if (!LookupAccountName(null, accountName, sidBuffer, ref sidLength, domain, ref domainLength, out sidUse))
        {
            return false;
        }

        sid = Sid.FromBinary(sidBuffer);
        return true;
    }

    /// <summary>The local account domain SID (S-1-5-21-a-b-c) shared by every local account on this computer.</summary>
    public static Sid? GetMachineSid() => MachineSid.Value;

    private static Sid? LookupMachineSid() =>
        TryLookupName(Environment.MachineName, out var sid, out var use)
            && use == SidTypeDomain
            && sid.IdentifierAuthority == 5
            && sid.SubAuthorityCount == 4
            && sid.GetSubAuthority(0) == 21
            ? sid
            : null;
}
