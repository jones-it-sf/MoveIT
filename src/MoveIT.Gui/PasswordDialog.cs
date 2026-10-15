using System.Runtime.Versioning;

namespace MoveIT.Gui;

/// <summary>A small modal prompt for a new account's password, used when passwords are not generated.</summary>
[SupportedOSPlatform("windows")]
internal sealed class PasswordDialog : Form
{
    private readonly TextBox _password;

    private PasswordDialog(string accountName)
    {
        Text = "MoveIT";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(380, 120);

        var label = new Label
        {
            Text = $"Password for the new local account '{accountName}':",
            AutoSize = true,
            Location = new Point(12, 15),
        };

        _password = new TextBox
        {
            UseSystemPasswordChar = true,
            Location = new Point(12, 45),
            Width = 356,
        };

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(212, 80), Width = 75 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(293, 80), Width = 75 };

        Controls.Add(label);
        Controls.Add(_password);
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public static string? Prompt(IWin32Window owner, string accountName)
    {
        using var dialog = new PasswordDialog(accountName);
        return dialog.ShowDialog(owner) == DialogResult.OK && dialog._password.Text.Length > 0 ? dialog._password.Text : null;
    }
}
