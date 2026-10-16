#Requires -Version 5.1
<#
.SYNOPSIS
    Installs a MoveIT profile migration through an Iru Windows Custom App.

.DESCRIPTION
    Iru runs this script as SYSTEM after its native app-enforcement UI has allowed the user to
    choose Update now or exhausted the configured deferrals. The script logs off every interactive
    user session, waits for Windows to unload those profiles, and only then starts MoveIT.

    Configure explorer.exe as an open-app blocker in Iru. That makes the migration appear in the Iru
    tray and enables Iru's enforcement deadline and deferral UI. Closing Explorer is not itself a
    logoff, so this script uses the Windows Terminal Services API to perform the real logoff.

    Iru detection rule:
      File exists: C:\ProgramData\MoveIT\migration.done

.PARAMETER MakeAdmin
    Add each generated local account to the local Administrators group.

.PARAMETER LeaveEntra
    Leave Microsoft Entra ID after every profile migration succeeds.

.PARAMETER RestartAfterSuccess
    Ask MoveIT to schedule a restart after a successful migration. Recommended with -LeaveEntra.

.PARAMETER ProfileUnloadTimeoutSeconds
    Maximum time to wait for interactive sessions to end before failing safely. Default: 180.
#>
[CmdletBinding()]
param(
    [switch]$MakeAdmin,
    [switch]$LeaveEntra,
    [switch]$RestartAfterSuccess,
    [ValidateRange(30, 900)]
    [int]$ProfileUnloadTimeoutSeconds = 180,
    [string]$MoveItPath = (Join-Path $PSScriptRoot 'moveit.exe'),
    [string]$MarkerPath = (Join-Path $env:ProgramData 'MoveIT\migration.done')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$dataDir = Join-Path $env:ProgramData 'MoveIT'
$logDir = Join-Path $dataDir 'Logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$transcript = Join-Path $logDir ("iru-custom-app-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))
Start-Transcript -Path $transcript -Append | Out-Null

$exitCode = 1
try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    if (-not $identity.IsSystem) {
        throw 'This installer must run as SYSTEM. Set the Iru Custom App install context to System.'
    }

    if (Test-Path $MarkerPath) {
        Write-Host "MoveIT migration is already complete (marker $MarkerPath exists)."
        $exitCode = 0
    }
    else {
        if (-not (Test-Path $MoveItPath)) {
            throw "moveit.exe not found at $MoveItPath."
        }

        if (-not ('MoveIt.WtsSessions' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MoveIt
{
    public sealed class UserSession
    {
        public int Id { get; set; }
        public string UserName { get; set; }
        public string Domain { get; set; }
        public string StationName { get; set; }
        public string DisplayName
        {
            get { return String.IsNullOrEmpty(Domain) ? UserName : Domain + "\\" + UserName; }
        }
    }

    public static class WtsSessions
    {
        private enum WTS_CONNECTSTATE_CLASS
        {
            WTSActive, WTSConnected, WTSConnectQuery, WTSShadow, WTSDisconnected,
            WTSIdle, WTSListen, WTSReset, WTSDown, WTSInit
        }

        private enum WTS_INFO_CLASS
        {
            WTSInitialProgram, WTSApplicationName, WTSWorkingDirectory, WTSOEMId,
            WTSSessionId, WTSUserName, WTSWinStationName, WTSDomainName
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WTS_SESSION_INFO
        {
            public int SessionId;
            public IntPtr StationName;
            public WTS_CONNECTSTATE_CLASS State;
        }

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSEnumerateSessions(
            IntPtr server, int reserved, int version, out IntPtr sessions, out int count);

        [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WTSQuerySessionInformation(
            IntPtr server, int sessionId, WTS_INFO_CLASS infoClass, out IntPtr buffer, out int bytes);

        [DllImport("wtsapi32.dll")]
        private static extern void WTSFreeMemory(IntPtr memory);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSLogoffSession(IntPtr server, int sessionId, bool wait);

        private static string Query(int sessionId, WTS_INFO_CLASS infoClass)
        {
            IntPtr buffer = IntPtr.Zero;
            int bytes;
            try
            {
                if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, infoClass, out buffer, out bytes)
                    || buffer == IntPtr.Zero || bytes <= 2)
                {
                    return String.Empty;
                }
                return Marshal.PtrToStringUni(buffer) ?? String.Empty;
            }
            finally
            {
                if (buffer != IntPtr.Zero) WTSFreeMemory(buffer);
            }
        }

        public static UserSession[] GetUserSessions()
        {
            IntPtr sessions = IntPtr.Zero;
            int count;
            if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out sessions, out count))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            try
            {
                var result = new List<UserSession>();
                int size = Marshal.SizeOf(typeof(WTS_SESSION_INFO));
                for (int index = 0; index < count; index++)
                {
                    IntPtr item = IntPtr.Add(sessions, index * size);
                    var info = (WTS_SESSION_INFO)Marshal.PtrToStructure(item, typeof(WTS_SESSION_INFO));
                    string user = Query(info.SessionId, WTS_INFO_CLASS.WTSUserName);
                    if (String.IsNullOrWhiteSpace(user)) continue;

                    result.Add(new UserSession
                    {
                        Id = info.SessionId,
                        UserName = user,
                        Domain = Query(info.SessionId, WTS_INFO_CLASS.WTSDomainName),
                        StationName = Marshal.PtrToStringUni(info.StationName) ?? String.Empty
                    });
                }
                return result.ToArray();
            }
            finally
            {
                if (sessions != IntPtr.Zero) WTSFreeMemory(sessions);
            }
        }

        public static void Logoff(int sessionId)
        {
            if (!WTSLogoffSession(IntPtr.Zero, sessionId, true))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}
'@
        }

        $sessions = @([MoveIt.WtsSessions]::GetUserSessions())
        foreach ($session in $sessions) {
            Write-Host "Logging off $($session.DisplayName) (session $($session.Id))..."
            [MoveIt.WtsSessions]::Logoff($session.Id)
        }

        $deadline = (Get-Date).AddSeconds($ProfileUnloadTimeoutSeconds)
        do {
            $remaining = @([MoveIt.WtsSessions]::GetUserSessions())
            if ($remaining.Count -eq 0) { break }
            if ((Get-Date) -ge $deadline) {
                $names = ($remaining | ForEach-Object { $_.DisplayName }) -join ', '
                throw "Timed out waiting for user sessions to log off: $names. MoveIT was not started."
            }
            Start-Sleep -Seconds 2
        } while ($true)

        # Give Windows a short grace period to finish unloading NTUSER.DAT and UsrClass.dat.
        Start-Sleep -Seconds 5

        $moveArgs = @('migrate', '--all', '--generate-passwords', '--yes')
        if ($MakeAdmin) { $moveArgs += '--admin' }
        if ($LeaveEntra) { $moveArgs += '--leave-entra' }
        if ($RestartAfterSuccess) { $moveArgs += '--reboot' }

        Write-Host "Running: $MoveItPath $($moveArgs -join ' ')"
        & $MoveItPath @moveArgs
        $moveItExitCode = $LASTEXITCODE
        Write-Host "MoveIT exit code: $moveItExitCode"

        if ($moveItExitCode -ne 0 -and $moveItExitCode -ne 2) {
            throw "MoveIT failed with exit code $moveItExitCode. See $logDir."
        }

        $markerDirectory = Split-Path -Parent $MarkerPath
        New-Item -ItemType Directory -Force -Path $markerDirectory | Out-Null
        @{
            completedAt = (Get-Date).ToString('o')
            computer = $env:COMPUTERNAME
            exitCode = $moveItExitCode
            deployment = 'Iru Windows Custom App'
        } | ConvertTo-Json | Set-Content -Path $MarkerPath -Encoding UTF8

        Write-Host "Migration completed successfully. Detection marker written to $MarkerPath."
        $exitCode = 0
    }
}
catch {
    Write-Host "Iru Custom App migration error: $($_.Exception.Message)"
    $exitCode = 1
}
finally {
    Stop-Transcript | Out-Null
}

exit $exitCode
