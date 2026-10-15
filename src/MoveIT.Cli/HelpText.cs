namespace MoveIT.Cli;

internal static class HelpText
{
    public const string Full = """
        MoveIT: move Windows user profiles to another account in place, without copying files.

        USAGE
          moveit list [--all]
          moveit migrate --source <who> --target <local user> [options]
          moveit migrate --config <file.json> [--what-if] [--yes] [--verbose] [--log <file>]
          moveit rollback --backup <folder> [--delete-created-account] [--yes]
          moveit sid --object-id <Entra object ID>  |  moveit sid --sid <S-1-12-1-...>
          moveit version

        LIST
          Shows the user profiles on this computer, their account, type (Entra, Local, Domain)
          and whether they are loaded. --all also shows system and service profiles.

        MIGRATE
          --source <who>              Profile to move. Any of: AzureAD\JohnSmith, JohnSmith,
                                      a SID, an Entra object ID, or a path like C:\Users\JohnSmith.
          --target <local user>       Local account that takes over the profile: jsmith or .\jsmith.
          --create                    Create the local account if it does not exist.
          --password-env <VAR>        Read the new account's password from an environment variable.
                                      Without it you are prompted (interactive sessions only).
          --full-name <name>          Full name for the local account.
          --admin                     Add the local account to the Administrators group.
          --must-change-password      Require a new password at first sign-in.
          --overwrite-target-profile  Replace the target's existing profile registration.
          --acl-mode add|replace      add (default): the old account keeps its permissions.
                                      replace: remove them after the profile is reassigned.
          --leave-entra               Remove the device from Entra ID after all profiles succeed.
          --reboot                    Restart 60 seconds after a successful migration.
          --force                     Allow --leave-entra with no enabled local administrator left.
          --backup-root <folder>      Default: %ProgramData%\MoveIT\Backups
          --log <file>                Default: %ProgramData%\MoveIT\Logs\moveit-<time>.log
          --what-if                   Run every check and scan, change nothing.
          --yes                       Do not ask for confirmation (required when non-interactive).
          --verbose                   Show debug output on screen (it is always in the log file).

        ROLLBACK
          --backup <folder>           Backup folder printed by migrate.
          --delete-created-account    Also delete the local account if migrate created it.
          Leaving Entra ID cannot be rolled back; rejoin the device instead.

        EXAMPLES
          moveit migrate --source AzureAD\JohnSmith --target jsmith --create --admin --what-if
          moveit migrate --source AzureAD\JohnSmith --target jsmith --create --password-env PW --leave-entra --yes
          moveit migrate --config C:\MoveIT\entra-to-local.json --yes

        EXIT CODES
          0 success   1 failed   2 succeeded with warnings   3 bad arguments or config
          4 pre-flight checks failed or cancelled (nothing was changed)

        Run as administrator, or as SYSTEM from an RMM tool. Users being migrated must be signed out.
        """;
}
