using MoveIT.Core.Accounts;

namespace MoveIT.Core.Migration;

public enum NameBasis
{
    /// <summary>The account name when known, otherwise the profile folder name.</summary>
    Auto,

    /// <summary>The account name after the domain, and before '@' for a UPN (AzureAD\john.smith@x -> john.smith).</summary>
    AccountName,

    /// <summary>The profile folder name (C:\Users\john.smi -> john.smi).</summary>
    FolderName,
}

/// <summary>How MoveIT invents a local account name when a mapping does not specify one.</summary>
public sealed class NamingOptions
{
    public NameBasis Basis { get; set; } = NameBasis.Auto;

    public bool Lowercase { get; set; } = true;

    public string Prefix { get; set; } = "";

    public int MaxLength { get; set; } = LocalAccountName.MaxLength;
}
