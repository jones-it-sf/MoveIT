using System.Runtime.Versioning;
using MoveIT.Core;
using MoveIT.Core.Logging;
using MoveIT.Core.Migration;
using MoveIT.Core.Profiles;
using MoveIT.Core.Security;
using MoveIT.Core.Util;

namespace MoveIT.Gui;

[SupportedOSPlatform("windows")]
internal sealed class MainForm : Form
{
    private readonly ListView _profiles = new();
    private readonly TextBox _prefix = new();
    private readonly ComboBox _aclMode = new();
    private readonly CheckBox _makeAdmin = new();
    private readonly CheckBox _generatePasswords = new();
    private readonly CheckBox _leaveEntra = new();
    private readonly CheckBox _reboot = new();
    private readonly CheckBox _force = new();
    private readonly Button _refresh = new();
    private readonly Button _preview = new();
    private readonly Button _migrate = new();
    private readonly Button _rollback = new();
    private readonly TextBox _log = new();
    private readonly Label _status = new();

    public MainForm()
    {
        Text = $"MoveIT Wizard {ToolInfo.Version}";
        MinimumSize = new Size(880, 620);
        Size = new Size(960, 680);
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();
        Load += (_, _) => LoadProfiles();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(8) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 230));

        var title = new Label
        {
            Text = "Move Windows user profiles to local accounts",
            Font = new Font(Font.FontFamily, 13, FontStyle.Bold),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
        };
        root.Controls.Add(title, 0, 0);
        root.SetColumnSpan(title, 2);

        _profiles.View = View.Details;
        _profiles.CheckBoxes = true;
        _profiles.FullRowSelect = true;
        _profiles.GridLines = true;
        _profiles.Dock = DockStyle.Fill;
        _profiles.Columns.Add("Account", 200);
        _profiles.Columns.Add("Type", 90);
        _profiles.Columns.Add("Loaded", 60);
        _profiles.Columns.Add("Profile folder", 230);
        root.Controls.Add(_profiles, 0, 1);

        root.Controls.Add(BuildOptionsPanel(), 1, 1);

        var logPanel = new Panel { Dock = DockStyle.Fill };
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.Dock = DockStyle.Fill;
        _log.BackColor = Color.White;
        _log.Font = new Font(FontFamily.GenericMonospace, 9);
        _status.Dock = DockStyle.Bottom;
        _status.Height = 20;
        _status.Text = "Ready.";
        logPanel.Controls.Add(_log);
        logPanel.Controls.Add(_status);
        root.Controls.Add(logPanel, 0, 2);
        root.SetColumnSpan(logPanel, 2);

        Controls.Add(root);
    }

    private FlowLayoutPanel BuildOptionsPanel()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };

        panel.Controls.Add(new Label { Text = "New local account name prefix (optional):", AutoSize = true, Margin = new Padding(3, 8, 3, 0) });
        _prefix.Width = 290;
        panel.Controls.Add(_prefix);

        _makeAdmin.Text = "Make the new accounts administrators";
        _makeAdmin.AutoSize = true;
        _generatePasswords.Text = "Generate strong passwords (saved to a file)";
        _generatePasswords.Checked = true;
        _generatePasswords.AutoSize = true;
        panel.Controls.Add(_makeAdmin);
        panel.Controls.Add(_generatePasswords);

        panel.Controls.Add(new Label { Text = "Old account permissions:", AutoSize = true, Margin = new Padding(3, 8, 3, 0) });
        _aclMode.DropDownStyle = ComboBoxStyle.DropDownList;
        _aclMode.Width = 290;
        _aclMode.Items.Add("Keep (safest)");
        _aclMode.Items.Add("Remove after migrating");
        _aclMode.SelectedIndex = 0;
        panel.Controls.Add(_aclMode);

        _leaveEntra.Text = "Leave Entra ID when finished";
        _leaveEntra.AutoSize = true;
        _reboot.Text = "Restart the computer when finished";
        _reboot.AutoSize = true;
        _force.Text = "Force (allow leaving Entra with no local admin)";
        _force.AutoSize = true;
        panel.Controls.Add(_leaveEntra);
        panel.Controls.Add(_reboot);
        panel.Controls.Add(_force);

        _refresh.Text = "Refresh profiles";
        _refresh.Width = 290;
        _refresh.Margin = new Padding(3, 14, 3, 3);
        _refresh.Click += (_, _) => LoadProfiles();

        _preview.Text = "Preview (what-if)";
        _preview.Width = 290;
        _preview.Click += async (_, _) => await RunAsync(whatIf: true);

        _migrate.Text = "Migrate selected";
        _migrate.Width = 290;
        _migrate.Font = new Font(Font, FontStyle.Bold);
        _migrate.Click += async (_, _) => await RunAsync(whatIf: false);

        _rollback.Text = "Roll back a migration...";
        _rollback.Width = 290;
        _rollback.Click += async (_, _) => await RollbackAsync();

        panel.Controls.Add(_refresh);
        panel.Controls.Add(_preview);
        panel.Controls.Add(_migrate);
        panel.Controls.Add(_rollback);

        return panel;
    }

    private void LoadProfiles()
    {
        try
        {
            _profiles.BeginUpdate();
            _profiles.Items.Clear();
            foreach (var profile in ProfileStore.GetProfiles(includeSystem: false))
            {
                var item = new ListViewItem(
                [
                    profile.AccountName ?? "(unresolved)",
                    profile.Kind + (profile.IsBackupEntry ? " .bak" : ""),
                    profile.IsLoaded ? "yes" : "no",
                    profile.ProfileImagePath,
                ])
                { Tag = profile, Checked = profile.Kind == SidKind.Entra && !profile.IsBackupEntry && !profile.IsLoaded };
                _profiles.Items.Add(item);
            }
            _status.Text = $"{_profiles.Items.Count} profile(s) found.";
        }
        catch (Exception e)
        {
            ShowError("Could not read the profiles on this computer.", e);
        }
        finally
        {
            _profiles.EndUpdate();
        }
    }

    private MigrationConfig? BuildConfig()
    {
        var selected = _profiles.Items.Cast<ListViewItem>()
            .Where(i => i.Checked)
            .Select(i => (ProfileEntry)i.Tag!)
            .ToList();

        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Select at least one profile to migrate.", "MoveIT", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        return new MigrationConfig
        {
            Profiles = selected.Select(p => new ProfileMapping
            {
                Source = p.Sid.Value,
                Target = "",
                CreateTarget = true,
                MakeAdmin = _makeAdmin.Checked,
            }).ToList(),
            Naming = new NamingOptions { Prefix = _prefix.Text.Trim() },
            GeneratePasswords = _generatePasswords.Checked,
            AclMode = _aclMode.SelectedIndex == 1 ? AclMode.Replace : AclMode.Add,
            LeaveEntra = _leaveEntra.Checked,
            Reboot = _reboot.Checked,
            Force = _force.Checked,
        };
    }

    private async Task RunAsync(bool whatIf)
    {
        var config = BuildConfig();
        if (config is null)
        {
            return;
        }

        if (!whatIf)
        {
            var warning = config.LeaveEntra
                ? "This will migrate the selected profiles and then remove this computer from Entra ID. Continue?"
                : "This will migrate the selected profiles. Continue?";
            if (MessageBox.Show(this, warning, "MoveIT", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
        }

        SetBusy(true);
        _log.Clear();
        var logPath = Path.Combine(AppDirectories.Ensure(AppDirectories.DefaultLogs), $"moveit-gui-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var logger = new Logger(logPath, verbose: false);
        logger.MessageLogged += AppendLog;

        try
        {
            await Task.Run(() =>
            {
                var engine = new MigrationEngine(config, logger, PromptPassword);
                var plan = engine.Prepare(whatIf);
                engine.Execute(plan, whatIf);
            });

            _status.Text = whatIf ? "Preview complete." : "Migration complete.";
            if (!whatIf)
            {
                MessageBox.Show(this, "The migration finished. Review the log for details.", "MoveIT", MessageBoxButtons.OK, MessageBoxIcon.Information);
                LoadProfiles();
            }
        }
        catch (MigrationException e)
        {
            AppendLog(LogLevel.Error, e.Message);
            foreach (var detail in e.Details)
            {
                AppendLog(LogLevel.Error, "  - " + detail);
            }
            _status.Text = "Stopped. Nothing was changed.";
            MessageBox.Show(this, e.Message, "MoveIT: checks failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception e)
        {
            ShowError("The migration did not complete.", e);
            _status.Text = "Error.";
        }
        finally
        {
            logger.MessageLogged -= AppendLog;
            logger.Dispose();
            SetBusy(false);
        }
    }

    private async Task RollbackAsync()
    {
        using var picker = new FolderBrowserDialog { Description = "Select the MoveIT backup folder to roll back", UseDescriptionForTitle = true };
        if (!Directory.Exists(AppDirectories.DefaultBackups))
        {
            AppDirectories.Ensure(AppDirectories.DefaultBackups);
        }
        picker.SelectedPath = AppDirectories.DefaultBackups;
        if (picker.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var backup = picker.SelectedPath;
        if (MessageBox.Show(this, $"Roll back the migration in:\n{backup}?", "MoveIT", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        SetBusy(true);
        _log.Clear();
        var logPath = Path.Combine(AppDirectories.Ensure(AppDirectories.DefaultLogs), $"moveit-gui-rollback-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var logger = new Logger(logPath, verbose: false);
        logger.MessageLogged += AppendLog;

        try
        {
            await Task.Run(() => RollbackRunner.Run(backup, deleteCreatedAccount: false, logger));
            _status.Text = "Rollback complete.";
            LoadProfiles();
        }
        catch (Exception e)
        {
            ShowError("The rollback did not complete.", e);
            _status.Text = "Error.";
        }
        finally
        {
            logger.MessageLogged -= AppendLog;
            logger.Dispose();
            SetBusy(false);
        }
    }

    private string? PromptPassword(string accountName) =>
        (string?)Invoke(new Func<string?>(() => PasswordDialog.Prompt(this, accountName)));

    private void AppendLog(LogLevel level, string message)
    {
        if (level == LogLevel.Debug)
        {
            return;
        }

        if (_log.InvokeRequired)
        {
            _log.BeginInvoke(() => AppendLog(level, message));
            return;
        }

        var prefix = level switch
        {
            LogLevel.Warning => "WARNING: ",
            LogLevel.Error => "ERROR: ",
            LogLevel.Success => "OK  ",
            _ => "",
        };
        _log.AppendText(prefix + message + Environment.NewLine);
    }

    private void SetBusy(bool busy)
    {
        _refresh.Enabled = !busy;
        _preview.Enabled = !busy;
        _migrate.Enabled = !busy;
        _rollback.Enabled = !busy;
        _profiles.Enabled = !busy;
        UseWaitCursor = busy;
        if (busy)
        {
            _status.Text = "Working...";
        }
    }

    private void ShowError(string context, Exception e)
    {
        AppendLog(LogLevel.Error, $"{context} {e.Message}");
        MessageBox.Show(this, $"{context}\n\n{e.Message}", "MoveIT", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
