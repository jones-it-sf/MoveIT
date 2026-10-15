using System.Runtime.Versioning;
using Microsoft.Win32;
using MoveIT.Core.Logging;
using MoveIT.Core.Migration;
using MoveIT.Core.Util;

namespace MoveIT.Core.Entra;

[SupportedOSPlatform("windows")]
public static class EntraJoin
{
    private const string IntuneProviderId = "MS DM Server";

    private static string Dsregcmd => Path.Combine(Environment.SystemDirectory, "dsregcmd.exe");

    public static EntraJoinStatus GetStatus() =>
        EntraJoinStatus.Parse(ProcessRunner.Run(Dsregcmd, ["/status"], TimeSpan.FromMinutes(1)).Output);

    public static bool IsMdmEnrolled()
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var enrollments = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Enrollments");
        if (enrollments is null)
        {
            return false;
        }

        foreach (var name in enrollments.GetSubKeyNames())
        {
            using var enrollment = enrollments.OpenSubKey(name);
            if (enrollment?.GetValue("ProviderID") is string provider && provider.Equals(IntuneProviderId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public static void Leave(Logger log)
    {
        log.Info("Removing this device from Entra ID (dsregcmd /leave)...");
        var result = ProcessRunner.Run(Dsregcmd, ["/leave"], TimeSpan.FromMinutes(2));
        if (!string.IsNullOrWhiteSpace(result.Output))
        {
            log.Debug(result.Output.Trim());
        }

        if (GetStatus().AzureAdJoined)
        {
            throw new MigrationException(
                $"dsregcmd /leave did not remove the device from Entra ID (exit code {result.ExitCode}). " +
                "Run MoveIT as SYSTEM (for example from your RMM tool) or disconnect the device under Settings > Accounts > Access work or school.");
        }

        log.Success("This device is no longer joined to Entra ID.");
    }
}
