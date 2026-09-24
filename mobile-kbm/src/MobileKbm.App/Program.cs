namespace MobileKbm.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // One per Windows session: two copies would keep replacing each other's session.
        using var instance = new Mutex(initiallyOwned: true, @"Local\MobileKbm.SingleInstance", out var first);
        if (!first)
        {
            MessageBox.Show(
                "Mobile KBM is already running — look for it in the system tray.",
                "Mobile KBM",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        // Per-monitor DPI awareness: screen sizes are real pixels, so pointer speed is right on every display.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        using var app = new TrayApp();
        Application.Run(app);
    }
}
