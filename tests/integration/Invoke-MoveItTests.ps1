#Requires -Version 5.1
<#
.SYNOPSIS
    End-to-end integration tests for MoveIT, run on a real Windows machine.

.DESCRIPTION
    These tests exercise the parts that cannot run off Windows: rewriting file and registry
    permissions, loading a user's registry hive, creating and cleaning up local accounts, and
    repointing the ProfileList so an account takes over an existing profile folder.

    Rather than needing a real Entra ID profile, each test creates a local source account and
    materialises a real profile for it with the Windows CreateProfile API, then migrates that
    profile to a new local account. This drives the exact same engine that an Entra -> local
    migration uses; only the source account type differs.

    Run on a DISPOSABLE test VM, in an elevated PowerShell session. Snapshot first.

.PARAMETER MoveItPath
    Path to moveit.exe. Defaults to the published build for this machine's architecture.

.PARAMETER KeepArtifacts
    Do not delete the test accounts, profiles and backups at the end (for debugging).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Invoke-MoveItTests.ps1
#>
[CmdletBinding()]
param(
    [string]$MoveItPath,
    [switch]$KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Test framework -------------------------------------------------------------------------

$script:Passed = 0
$script:Failed = 0
$script:Failures = @()

function Write-Section($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }

function Assert-That {
    param([Parameter(Mandatory)][bool]$Condition, [Parameter(Mandatory)][string]$Because)
    if ($Condition) {
        $script:Passed++
        Write-Host "  [pass] $Because" -ForegroundColor Green
    } else {
        $script:Failed++
        $script:Failures += $Because
        Write-Host "  [FAIL] $Because" -ForegroundColor Red
    }
}

function Invoke-Test {
    param([string]$Name, [scriptblock]$Body)
    Write-Section $Name
    try {
        & $Body
    } catch {
        $script:Failed++
        $script:Failures += "$Name threw: $($_.Exception.Message)"
        Write-Host "  [FAIL] threw: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
    }
}

# --- Windows helpers ------------------------------------------------------------------------

Add-Type -Namespace MoveItTests -Name Userenv -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("userenv.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode, SetLastError=true)]
public static extern int CreateProfile(string pszUserSid, string pszUserName, System.Text.StringBuilder pszProfilePath, uint cchProfilePath);

[System.Runtime.InteropServices.DllImport("userenv.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode, SetLastError=true)]
public static extern bool DeleteProfile(string pszSidString, string pszProfilePath, string pszComputerName);
'@

$ProfileListKey = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList'

function New-TestUser {
    param([string]$Name, [string]$Password)
    net user $Name $Password /add /y | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "net user $Name /add failed ($LASTEXITCODE)" }
    return (New-Object System.Security.Principal.NTAccount($env:COMPUTERNAME, $Name)
        ).Translate([System.Security.Principal.SecurityIdentifier]).Value
}

function New-RealProfile {
    param([string]$Sid, [string]$Name)
    $sb = New-Object System.Text.StringBuilder 1024
    $hr = [MoveItTests.Userenv]::CreateProfile($Sid, $Name, $sb, $sb.Capacity)
    if ($hr -ne 0) { throw "CreateProfile for $Name failed (HRESULT 0x{0:X8})" -f $hr }
    return $sb.ToString()
}

function Get-ProfileImagePath {
    param([string]$Sid)
    $key = Join-Path $ProfileListKey $Sid
    if (-not (Test-Path $key)) { return $null }
    return (Get-ItemProperty -Path $key -Name ProfileImagePath -ErrorAction SilentlyContinue).ProfileImagePath
}

function Test-AclReferencesSid {
    param([string]$Path, [string]$Sid)
    $out = & icacls $Path 2>$null
    return ($out -join "`n") -match [regex]::Escape($Sid)
}

function Test-HiveReferencesSid {
    param([string]$ProfilePath, [string]$Sid)
    $hive = Join-Path $ProfilePath 'NTUSER.DAT'
    $mount = "HKLM\MoveItVerify_$(Get-Random)"
    & reg load $mount $hive *> $null
    if ($LASTEXITCODE -ne 0) { return $null }   # in use; skip this assertion
    try {
        $acl = & reg query $mount *> $null       # touch so the load is real
        $found = $false
        $sddl = (Get-Acl "Registry::$($mount)").Sddl
        return $sddl -match [regex]::Escape($Sid)
    } finally {
        [GC]::Collect(); [GC]::WaitForPendingFinalizers()
        & reg unload $mount *> $null
    }
}

function Resolve-MoveItPath {
    if ($MoveItPath) { return (Resolve-Path $MoveItPath).Path }
    $arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
    $candidate = Join-Path $PSScriptRoot "..\..\publish\$arch\moveit.exe"
    if (-not (Test-Path $candidate)) {
        throw "moveit.exe not found at $candidate. Build it first (dotnet publish) or pass -MoveItPath."
    }
    return (Resolve-Path $candidate).Path
}

function Invoke-MoveIt {
    param([string[]]$MoveItArgs, [hashtable]$Env)
    if ($Env) { foreach ($k in $Env.Keys) { Set-Item "Env:$k" $Env[$k] } }
    try {
        $output = & $script:Exe @MoveItArgs 2>&1
        $output | ForEach-Object { Write-Host "      $_" -ForegroundColor DarkGray }
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output -join "`n") }
    } finally {
        if ($Env) { foreach ($k in $Env.Keys) { Remove-Item "Env:$k" -ErrorAction SilentlyContinue } }
    }
}

# --- Pre-flight -----------------------------------------------------------------------------

$admin = [Security.Principal.WindowsPrincipal]::new(
    [Security.Principal.WindowsIdentity]::GetCurrent()
).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { throw 'Run this script in an elevated (Run as administrator) PowerShell session.' }

$script:Exe = Resolve-MoveItPath
Write-Host "Testing: $script:Exe"
Write-Host "Version: $((& $script:Exe version) -join '')"

$suffix = Get-Random -Maximum 9999
$created = [System.Collections.Generic.List[string]]::new()   # account names to delete
$profilePaths = [System.Collections.Generic.List[string]]::new()
$backups = [System.Collections.Generic.List[string]]::new()
$sidsToClean = [System.Collections.Generic.List[string]]::new()
$password = 'Test-' + [guid]::NewGuid().ToString('N').Substring(0, 12) + '!aA1'

try {
    # --- Test 1: what-if changes nothing ---------------------------------------------------
    Invoke-Test 'what-if makes no changes' {
        $src = "mi_src1_$suffix"; $dst = "mi_dst1_$suffix"
        $sid = New-TestUser -Name $src -Password $password; $created.Add($src); $sidsToClean.Add($sid)
        $path = New-RealProfile -Sid $sid -Name $src; $profilePaths.Add($path)

        $r = Invoke-MoveIt @('migrate', '--source', $src, '--target', $dst, '--create', '--generate-passwords', '--what-if', '--yes')
        Assert-That ($r.ExitCode -eq 0) "what-if exit code is 0 (was $($r.ExitCode))"
        Assert-That (-not (Get-LocalUser -Name $dst -ErrorAction SilentlyContinue)) "target account was not created by what-if"
        Assert-That ($null -ne (Get-ProfileImagePath -Sid $sid)) "source profile registration still present after what-if"
    }

    # --- Test 2: full migration + rollback -------------------------------------------------
    Invoke-Test 'migrate then rollback (acl-mode add)' {
        $src = "mi_src2_$suffix"; $dst = "mi_dst2_$suffix"
        $srcSid = New-TestUser -Name $src -Password $password; $created.Add($src); $sidsToClean.Add($srcSid)
        $path = New-RealProfile -Sid $srcSid -Name $src; $profilePaths.Add($path)
        Set-Content -Path (Join-Path $path 'marker.txt') -Value 'hello' -Force

        $r = Invoke-MoveIt @('migrate', '--source', $src, '--target', $dst, '--create', '--generate-passwords', '--yes')
        $created.Add($dst)
        $dstSid = (Get-LocalUser -Name $dst -ErrorAction SilentlyContinue)?.SID.Value
        if ($dstSid) { $sidsToClean.Add($dstSid) }

        Assert-That ($r.ExitCode -eq 0) "migrate exit code is 0 (was $($r.ExitCode))"
        Assert-That ($null -ne $dstSid) "target account was created"
        Assert-That ((Get-ProfileImagePath -Sid $dstSid) -eq $path) "profile folder is now registered to the target"
        Assert-That ($null -eq (Get-ProfileImagePath -Sid $srcSid)) "source profile registration was removed"
        Assert-That (Test-AclReferencesSid -Path $path -Sid $dstSid) "profile folder ACL references the target SID"
        Assert-That (Test-Path (Join-Path $path 'marker.txt')) "no files were copied or deleted (marker.txt still there)"
        $hiveOk = Test-HiveReferencesSid -ProfilePath $path -Sid $dstSid
        if ($null -ne $hiveOk) { Assert-That $hiveOk "NTUSER.DAT ACL references the target SID" }

        $backup = ([regex]'backup: (.+)').Match($r.Output).Groups[1].Value.Trim()
        if ($backup) { $backups.Add($backup) }
        Assert-That ($backup -and (Test-Path $backup)) "a backup folder was created"

        $rb = Invoke-MoveIt @('rollback', '--backup', $backup, '--delete-created-account', '--yes')
        Assert-That ($rb.ExitCode -eq 0) "rollback exit code is 0 (was $($rb.ExitCode))"
        Assert-That ((Get-ProfileImagePath -Sid $srcSid) -eq $path) "rollback restored the source profile registration"
        Assert-That ($null -eq (Get-ProfileImagePath -Sid $dstSid)) "rollback removed the target profile registration"
        Assert-That (-not (Get-LocalUser -Name $dst -ErrorAction SilentlyContinue)) "rollback deleted the account it created"
    }

    # --- Test 3: disable-source -------------------------------------------------------------
    Invoke-Test 'disable-source disables the old local account' {
        $src = "mi_src3_$suffix"; $dst = "mi_dst3_$suffix"
        $srcSid = New-TestUser -Name $src -Password $password; $created.Add($src); $sidsToClean.Add($srcSid)
        $path = New-RealProfile -Sid $srcSid -Name $src; $profilePaths.Add($path)

        $r = Invoke-MoveIt @('migrate', '--source', $src, '--target', $dst, '--create', '--generate-passwords', '--disable-source', '--yes')
        $created.Add($dst)
        $dstSid = (Get-LocalUser -Name $dst -ErrorAction SilentlyContinue)?.SID.Value
        if ($dstSid) { $sidsToClean.Add($dstSid) }

        Assert-That ($r.ExitCode -eq 0) "migrate exit code is 0 (was $($r.ExitCode))"
        Assert-That (-not (Get-LocalUser -Name $src).Enabled) "source account is disabled"
    }

    # --- Test 4: pre-flight refuses a loaded / bad source ----------------------------------
    Invoke-Test 'pre-flight refuses an unknown source and changes nothing' {
        $r = Invoke-MoveIt @('migrate', '--source', "does_not_exist_$suffix", '--target', "mi_none_$suffix", '--create', '--generate-passwords', '--yes')
        Assert-That ($r.ExitCode -eq 4) "exit code is 4 (nothing changed) for an unknown source (was $($r.ExitCode))"
    }

    # --- Test 5: list shows a migrated profile ---------------------------------------------
    Invoke-Test 'list reports profiles' {
        $r = Invoke-MoveIt @('list')
        Assert-That ($r.ExitCode -eq 0) "list exit code is 0"
        Assert-That ($r.Output -match 'TYPE\s+ACCOUNT') "list prints a header row"
    }
}
finally {
    if (-not $KeepArtifacts) {
        Write-Section 'Cleanup'
        foreach ($name in ($created | Select-Object -Unique)) {
            net user $name /delete *> $null
        }
        foreach ($sid in ($sidsToClean | Select-Object -Unique)) {
            $p = Get-ProfileImagePath -Sid $sid
            [MoveItTests.Userenv]::DeleteProfile($sid, $null, $null) | Out-Null
            if ($p -and (Test-Path $p) -and $p -like '*\Users\mi_*') { Remove-Item $p -Recurse -Force -ErrorAction SilentlyContinue }
            Remove-Item (Join-Path $ProfileListKey $sid) -Recurse -Force -ErrorAction SilentlyContinue
        }
        foreach ($b in ($backups | Select-Object -Unique)) {
            if ($b -and (Test-Path $b)) { Remove-Item $b -Recurse -Force -ErrorAction SilentlyContinue }
        }
        Write-Host '  done'
    } else {
        Write-Host "`nKeeping artifacts. Accounts: $($created -join ', ')"
    }
}

Write-Section 'Summary'
Write-Host ("Passed: {0}  Failed: {1}" -f $script:Passed, $script:Failed) -ForegroundColor ($(if ($script:Failed) { 'Red' } else { 'Green' }))
if ($script:Failed) {
    $script:Failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
exit 0
