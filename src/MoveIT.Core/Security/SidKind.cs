namespace MoveIT.Core.Security;

public enum SidKind
{
    Local,
    Domain,
    Entra,
    System,

    /// <summary>An S-1-5-21 account when the machine SID could not be determined.</summary>
    Unknown,
}

public static class SidClassifier
{
    public static SidKind Classify(Sid sid, Sid? machineSid)
    {
        if (sid.IsEntraUser)
        {
            return SidKind.Entra;
        }

        var domain = sid.AccountDomainSid;
        if (domain is null)
        {
            return SidKind.System;
        }

        if (machineSid is null)
        {
            return SidKind.Unknown;
        }

        return domain == machineSid ? SidKind.Local : SidKind.Domain;
    }
}
