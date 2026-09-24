using System.Diagnostics;

namespace MobileKbm.App;

/// <summary>Asks for the driver key — the app's one real setting.</summary>
internal sealed class DriverKeyForm : Form
{
    private readonly TextBox _key;

    public DriverKeyForm(string baseUrl, bool hasKey)
    {
        Text = "Mobile KBM — driver key";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var intro = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            Text = (hasKey ? "Enter a new driver key to replace the current one. " : "")
                   + "Mobile KBM needs a driver key to open sessions for your phone. Make one on the "
                   + "Gamepad website under Driver keys (log in, name it, copy it — it is shown once).\n\n"
                   + "Then open the same website on your phone, logged in with that account: this PC's "
                   + "session is listed there — no code needed.",
        };

        var link = new LinkLabel { AutoSize = true, Text = baseUrl };
        link.LinkClicked += (_, _) => Browser.Open(baseUrl);

        _key = new TextBox { Width = 440, UseSystemPasswordChar = true, PlaceholderText = "gpk_…" };

        var reveal = new CheckBox { AutoSize = true, Text = "Show key" };
        reveal.CheckedChanged += (_, _) => _key.UseSystemPasswordChar = !reveal.Checked;

        var save = new Button { AutoSize = true, Text = "Save", DialogResult = DialogResult.OK, Enabled = false };
        var cancel = new Button { AutoSize = true, Text = "Cancel", DialogResult = DialogResult.Cancel };
        _key.TextChanged += (_, _) => save.Enabled = IsPlausible(DriverKey);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
        };
        layout.Controls.Add(intro);
        layout.Controls.Add(link);
        layout.Controls.Add(_key);
        layout.Controls.Add(reveal);
        layout.Controls.Add(buttons);
        Controls.Add(layout);

        AcceptButton = save;
        CancelButton = cancel;
    }

    public string DriverKey => _key.Text.Trim();

    /// <summary>A key is one token; anything with spaces inside is a copy-paste accident.</summary>
    private static bool IsPlausible(string key) => key.Length >= 8 && !key.Any(char.IsWhiteSpace);
}

internal static class Browser
{
    public static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No default browser: nothing sensible to do.
        }
    }
}
