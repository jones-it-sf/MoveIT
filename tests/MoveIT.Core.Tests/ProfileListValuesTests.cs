using Microsoft.Win32;
using MoveIT.Core.Profiles;
using MoveIT.Core.Security;

namespace MoveIT.Core.Tests;

public class ProfileListValuesTests
{
    private static readonly Sid Source = Sid.FromEntraObjectId(Guid.NewGuid());
    private static readonly Sid Target = Sid.Parse("S-1-5-21-1-2-3-1001");

    private static List<RegistryValueData> SourceValues() =>
    [
        new("ProfileImagePath", RegistryValueKind.ExpandString, @"C:\Users\JohnSmith"),
        new("Flags", RegistryValueKind.DWord, 0),
        new("State", RegistryValueKind.DWord, 256),
        new("RefCount", RegistryValueKind.DWord, 3),
        new("Sid", RegistryValueKind.Binary, Source.ToArray()),
        new("Guid", RegistryValueKind.String, "{5c5a3a4e-8f1b-4d2a-9b55-0e7a1c3f9d21}"),
        new("CentralProfile", RegistryValueKind.String, ""),
        new("FullProfile", RegistryValueKind.DWord, 1),
        new("LocalProfileLoadTimeLow", RegistryValueKind.DWord, 123456),
    ];

    private static RegistryValueData Value(IReadOnlyList<RegistryValueData> values, string name) =>
        values.Single(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void PointsTheEntryAtTheTargetAccount()
    {
        var values = ProfileListValues.ForLocalTarget(SourceValues(), Target);

        Assert.Equal(Target.ToArray(), (byte[])Value(values, "Sid").Data);
        Assert.Equal(RegistryValueKind.Binary, Value(values, "Sid").Kind);
        Assert.Single(values, v => v.Name == "Sid");
    }

    [Fact]
    public void KeepsTheProfileFolderAndHistory()
    {
        var values = ProfileListValues.ForLocalTarget(SourceValues(), Target);

        Assert.Equal(@"C:\Users\JohnSmith", Value(values, "ProfileImagePath").Data);
        Assert.Equal(RegistryValueKind.ExpandString, Value(values, "ProfileImagePath").Kind);
        Assert.Equal(1, Value(values, "FullProfile").Data);
        Assert.Equal(123456, Value(values, "LocalProfileLoadTimeLow").Data);
    }

    [Fact]
    public void ResetsStateAndDropsDomainOnlyValues()
    {
        var values = ProfileListValues.ForLocalTarget(SourceValues(), Target);

        Assert.Equal(0, Value(values, "State").Data);
        Assert.Equal(0, Value(values, "RefCount").Data);
        Assert.DoesNotContain(values, v => v.Name is "Guid" or "CentralProfile");
    }

    [Fact]
    public void AddsStateAndFlagsWhenMissing()
    {
        var values = ProfileListValues.ForLocalTarget([new("ProfileImagePath", RegistryValueKind.ExpandString, @"C:\Users\x")], Target);

        Assert.Equal(0, Value(values, "State").Data);
        Assert.Equal(0, Value(values, "Flags").Data);
    }

    [Fact]
    public void RequiresAProfilePath()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ProfileListValues.ForLocalTarget([new("State", RegistryValueKind.DWord, 0)], Target));
    }
}
