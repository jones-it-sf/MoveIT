using MoveIT.Core.Migration;
using MoveIT.Core.Security;

namespace MoveIT.Core.Tests;

public class MigrationConfigTests
{
    private const string Machine = "PC01";

    [Fact]
    public void SampleConfigIsValid()
    {
        var config = MigrationConfigLoader.Load(Path.Combine(RepositoryRoot(), "samples", "entra-to-local.json"));

        Assert.Equal(2, config.Profiles.Count);
        Assert.Equal(@"AzureAD\JohnSmith", config.Profiles[0].Source);
        Assert.Equal("MOVEIT_PASSWORD_JSMITH", config.Profiles[0].PasswordEnv);
        Assert.True(config.Profiles[0].MakeAdmin);
        Assert.True(config.LeaveEntra);
        Assert.Equal(AclMode.Add, config.AclMode);
        Assert.Empty(config.Validate(Machine));
    }

    [Fact]
    public void ParsesEnumsCaseInsensitively()
    {
        var config = MigrationConfigLoader.Parse("""{ "aclMode": "replace", "profiles": [] }""");

        Assert.Equal(AclMode.Replace, config.AclMode);
    }

    [Fact]
    public void RejectsUnknownSettings()
    {
        var error = Assert.Throws<ConfigException>(() => MigrationConfigLoader.Parse("""{ "leaveEntraa": true, "profiles": [] }"""));

        Assert.Contains("leaveEntraa", error.Message);
    }

    [Fact]
    public void RejectsInvalidJson()
    {
        Assert.Throws<ConfigException>(() => MigrationConfigLoader.Parse("{ \"profiles\": [ "));
    }

    [Fact]
    public void RequiresAtLeastOneProfile()
    {
        Assert.Contains(new MigrationConfig().Validate(Machine), e => e.Contains("At least one"));
    }

    [Fact]
    public void ReportsEveryProblem()
    {
        var config = new MigrationConfig
        {
            Profiles =
            [
                new ProfileMapping { Source = "", Target = "bad/name" },
                new ProfileMapping { Source = @"AzureAD\A", Target = @"CONTOSO\a" },
                new ProfileMapping { Source = @"AzureAD\B", Target = "b", Password = "x", PasswordEnv = "Y", CreateTarget = true },
                new ProfileMapping { Source = @"AzureAD\C", Target = "c", PasswordEnv = "Y" },
                new ProfileMapping { Source = @"azuread\c", Target = @".\B" },
            ],
        };

        var errors = config.Validate(Machine);

        Assert.Contains(errors, e => e.StartsWith("profiles[0]: 'source' is required"));
        Assert.Contains(errors, e => e.StartsWith("profiles[0]") && e.Contains("characters Windows does not allow"));
        Assert.Contains(errors, e => e.StartsWith("profiles[1]") && e.Contains("not a local account"));
        Assert.Contains(errors, e => e.StartsWith("profiles[2]") && e.Contains("not both"));
        Assert.Contains(errors, e => e.StartsWith("profiles[3]") && e.Contains("only used when 'createTarget'"));
        Assert.Contains(errors, e => e.StartsWith("profiles[4]") && e.Contains("source") && e.Contains("more than once"));
        Assert.Contains(errors, e => e.StartsWith("profiles[4]") && e.Contains("target 'B' appears more than once"));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MoveIT.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
