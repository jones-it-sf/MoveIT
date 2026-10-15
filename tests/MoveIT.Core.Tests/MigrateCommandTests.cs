using MoveIT.Cli;
using MoveIT.Cli.Commands;
using MoveIT.Core.Migration;

namespace MoveIT.Core.Tests;

public class MigrateCommandTests
{
    private static MigrationConfig Build(params string[] args) => MigrateCommand.BuildConfig(CommandLine.Parse(args));

    [Fact]
    public void BuildsSingleProfileFromFlags()
    {
        var config = Build("migrate", "--source", @"AzureAD\JohnSmith", "--target", "jsmith", "--create", "--admin");

        var mapping = Assert.Single(config.Profiles);
        Assert.Equal(@"AzureAD\JohnSmith", mapping.Source);
        Assert.Equal("jsmith", mapping.Target);
        Assert.True(mapping.CreateTarget);
        Assert.True(mapping.MakeAdmin);
        Assert.False(config.MigrateAllEntraProfiles);
    }

    [Fact]
    public void AllEnablesDiscoveryWithoutExplicitProfiles()
    {
        var config = Build("migrate", "--all", "--generate-passwords", "--name-prefix", "loc-", "--exclude", @"AzureAD\admin, S-1-12-1-1-2-3-4");

        Assert.True(config.MigrateAllEntraProfiles);
        Assert.True(config.GeneratePasswords);
        Assert.Equal("loc-", config.Naming.Prefix);
        Assert.Equal([@"AzureAD\admin", "S-1-12-1-1-2-3-4"], config.ExcludeSources);
        Assert.Empty(config.Profiles);
    }

    [Fact]
    public void LoadsMappingsFromCsv()
    {
        var csv = Path.GetTempFileName();
        File.WriteAllText(csv, "source,target\nAzureAD\\A,a\nAzureAD\\B,\n");
        try
        {
            var config = Build("migrate", "--map", csv, "--generate-passwords");

            Assert.Equal(2, config.Profiles.Count);
            Assert.Equal("a", config.Profiles[0].Target);
            Assert.Equal("", config.Profiles[1].Target);
        }
        finally
        {
            File.Delete(csv);
        }
    }

    [Fact]
    public void RequiresSomethingToMigrate()
    {
        Assert.Throws<CommandLineException>(() => Build("migrate", "--generate-passwords"));
    }

    [Fact]
    public void RejectsTargetWithoutSource()
    {
        Assert.Throws<CommandLineException>(() => Build("migrate", "--target", "jsmith"));
    }

    [Fact]
    public void RejectsBatchFlagsWithConfig()
    {
        Assert.Throws<CommandLineException>(() => Build("migrate", "--config", "x.json", "--all"));
    }
}
