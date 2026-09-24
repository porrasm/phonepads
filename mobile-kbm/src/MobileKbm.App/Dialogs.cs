using System.Diagnostics;

namespace MobileKbm.App;

/// <summary>The app's only window: the driver key, how fast the pointer moves, and starting with Windows.</summary>
internal sealed class SettingsForm : Form
{
    /// <summary>The slider's stops, slowest to fastest; 1× sits in the middle.</summary>
    private static readonly double[] Speeds = [0.25, 0.35, 0.5, 0.7, 1, 1.4, 2, 2.8, 4];

    private readonly TextBox _key;
    private readonly TrackBar _speed;
    private readonly CheckBox _startup;

    public SettingsForm(string baseUrl, bool hasKey, double pointerSpeed, bool startWithWindows)
    {
        Text = "Mobile KBM settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var width = LogicalToDeviceUnits(360);
        var gap = new Padding(0, LogicalToDeviceUnits(10), 0, 0);

        // Password boxes draw no placeholder, so the hint goes in the heading.
        var keyLabel = new Label
        {
            AutoSize = true,
            Text = hasKey ? "Driver key (saved; leave empty to keep it)" : "Driver key",
            Font = new Font(Font, FontStyle.Bold),
        };
        var link = new LinkLabel { AutoSize = true, Text = "Get one at " + new Uri(baseUrl).Host };
        link.LinkClicked += (_, _) => Browser.Open(baseUrl);

        _key = new TextBox
        {
            Width = width,
            UseSystemPasswordChar = true,
        };

        var phone = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(width, 0),
            ForeColor = SystemColors.GrayText,
            Text = "On your phone, open the same site logged in: this PC is listed there.",
        };

        var speedLabel = new Label { AutoSize = true, Text = "Pointer speed", Font = keyLabel.Font };
        var speedValue = new Label { AutoSize = true };
        _speed = new TrackBar
        {
            Width = width,
            Minimum = 0,
            Maximum = Speeds.Length - 1,
            TickStyle = TickStyle.BottomRight,
            Value = NearestStop(pointerSpeed),
        };
        _speed.ValueChanged += (_, _) => speedValue.Text = $"{PointerSpeed:0.##}×";
        speedValue.Text = $"{PointerSpeed:0.##}×";

        var speedRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = gap };
        speedRow.Controls.Add(speedLabel);
        speedRow.Controls.Add(speedValue);

        _startup = new CheckBox { AutoSize = true, Text = "Start with Windows", Checked = startWithWindows, Margin = gap };

        var save = new Button { AutoSize = true, Text = "Save", DialogResult = DialogResult.OK };
        var cancel = new Button { AutoSize = true, Text = "Cancel", DialogResult = DialogResult.Cancel };
        void Validate() => save.Enabled = DriverKey is null ? hasKey : IsPlausible(DriverKey);
        _key.TextChanged += (_, _) => Validate();
        Validate();

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = gap };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(LogicalToDeviceUnits(12)),
        };
        layout.Controls.Add(keyLabel);
        layout.Controls.Add(_key);
        layout.Controls.Add(link);
        layout.Controls.Add(phone);
        layout.Controls.Add(speedRow);
        layout.Controls.Add(_speed);
        layout.Controls.Add(_startup);
        layout.Controls.Add(buttons);
        Controls.Add(layout);

        AcceptButton = save;
        CancelButton = cancel;
    }

    /// <summary>The key typed in, or null when the box was left empty.</summary>
    public string? DriverKey => _key.Text.Trim() is { Length: > 0 } key ? key : null;

    public double PointerSpeed => Speeds[_speed.Value];

    public bool StartWithWindows => _startup.Checked;

    private static int NearestStop(double speed)
    {
        speed = double.IsFinite(speed) ? Math.Clamp(speed, Speeds[0], Speeds[^1]) : 1;
        var best = 0;
        for (var i = 1; i < Speeds.Length; i++)
        {
            if (Math.Abs(Math.Log(Speeds[i] / speed)) < Math.Abs(Math.Log(Speeds[best] / speed))) best = i;
        }

        return best;
    }

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
