using MoveIT.Core.Entra;

namespace MoveIT.Core.Tests;

public class EntraJoinStatusTests
{
    private const string JoinedOutput = """
        +----------------------------------------------------------------------+
        | Device State                                                         |
        +----------------------------------------------------------------------+

                     AzureAdJoined : YES
                  EnterpriseJoined : NO
                      DomainJoined : NO
                       Device Name : PC01

        +----------------------------------------------------------------------+
        | Device Details                                                       |
        +----------------------------------------------------------------------+

                          DeviceId : 11111111-2222-3333-4444-555555555555
                        Thumbprint : 0123456789ABCDEF

        +----------------------------------------------------------------------+
        | Tenant Details                                                       |
        +----------------------------------------------------------------------+

                        TenantName : Contoso
                          TenantId : aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee
        """;

    [Fact]
    public void ParsesAJoinedDevice()
    {
        var status = EntraJoinStatus.Parse(JoinedOutput.Replace("\n", "\r\n"));

        Assert.True(status.AzureAdJoined);
        Assert.False(status.DomainJoined);
        Assert.False(status.EnterpriseJoined);
        Assert.Equal("Contoso", status.TenantName);
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", status.TenantId);
        Assert.Equal("11111111-2222-3333-4444-555555555555", status.DeviceId);
    }

    [Fact]
    public void ParsesAHybridDevice()
    {
        var status = EntraJoinStatus.Parse(JoinedOutput.Replace("DomainJoined : NO", "DomainJoined : YES"));

        Assert.True(status.AzureAdJoined);
        Assert.True(status.DomainJoined);
    }

    [Fact]
    public void TreatsEmptyOutputAsNotJoined()
    {
        var status = EntraJoinStatus.Parse("");

        Assert.False(status.AzureAdJoined);
        Assert.Null(status.TenantName);
    }
}
