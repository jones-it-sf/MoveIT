using MoveIT.Core.Migration;

namespace MoveIT.Core.Tests;

public class MappingCsvTests
{
    [Fact]
    public void ParsesColumnsByName()
    {
        var mappings = MappingCsv.Parse("""
            source,target,fullName,admin,mustChangePassword,passwordEnv
            AzureAD\JohnSmith,jsmith,John Smith,yes,no,PW_JSMITH
            AzureAD\JaneDoe,,Jane Doe,,,
            """);

        Assert.Equal(2, mappings.Count);

        var john = mappings[0];
        Assert.Equal(@"AzureAD\JohnSmith", john.Source);
        Assert.Equal("jsmith", john.Target);
        Assert.Equal("John Smith", john.FullName);
        Assert.True(john.MakeAdmin);
        Assert.False(john.MustChangePassword);
        Assert.Equal("PW_JSMITH", john.PasswordEnv);
        Assert.True(john.CreateTarget);

        var jane = mappings[1];
        Assert.Equal("", jane.Target);
        Assert.Null(jane.PasswordEnv);
        Assert.False(jane.MakeAdmin);
    }

    [Fact]
    public void IgnoresColumnOrderAndCaseAndSpacing()
    {
        var mappings = MappingCsv.Parse("""
            Full Name,Target,Source
            John Smith,jsmith,AzureAD\JohnSmith
            """);

        Assert.Equal(@"AzureAD\JohnSmith", mappings[0].Source);
        Assert.Equal("jsmith", mappings[0].Target);
        Assert.Equal("John Smith", mappings[0].FullName);
    }

    [Fact]
    public void SkipsRowsWithNoSource()
    {
        var mappings = MappingCsv.Parse("source,target\n,orphan\nAzureAD\\A,a");

        Assert.Single(mappings);
        Assert.Equal(@"AzureAD\A", mappings[0].Source);
    }

    [Fact]
    public void RejectsUnknownColumns()
    {
        var error = Assert.Throws<ConfigException>(() => MappingCsv.Parse("source,wibble\nx,y"));

        Assert.Contains("wibble", error.Message);
    }

    [Fact]
    public void RequiresSourceColumn()
    {
        Assert.Throws<ConfigException>(() => MappingCsv.Parse("target,fullName\njsmith,John"));
    }

    [Fact]
    public void RejectsNonBooleanValues()
    {
        Assert.Throws<ConfigException>(() => MappingCsv.Parse("source,admin\nAzureAD\\A,maybe"));
    }

    [Fact]
    public void RejectsHeaderOnly()
    {
        Assert.Throws<ConfigException>(() => MappingCsv.Parse("source,target"));
    }
}
