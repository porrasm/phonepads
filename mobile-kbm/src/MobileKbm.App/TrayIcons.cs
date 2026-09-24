using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace MobileKbm.App;

/// <summary>
/// The tray icon, drawn at runtime in the colour of the current state — no icon files to
/// ship, and the state is readable at a glance.
/// </summary>
internal sealed class TrayIcons : IDisposable
{
    public static readonly Color Online = Color.FromArgb(46, 160, 67);
    public static readonly Color Busy = Color.FromArgb(210, 153, 34);
    public static readonly Color Problem = Color.FromArgb(207, 34, 46);
    public static readonly Color Paused = Color.FromArgb(110, 118, 129);

    private readonly Dictionary<Color, Icon> _icons = [];
    private readonly List<IntPtr> _handles = [];

    public Icon For(Color color)
    {
        if (_icons.TryGetValue(color, out var icon)) return icon;

        const int Size = 32;
        using var bitmap = new Bitmap(Size, Size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            using var fill = new SolidBrush(color);
            g.FillEllipse(fill, 1, 1, Size - 2, Size - 2);

            using var font = new Font("Segoe UI", 15, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("K", font, Brushes.White, new RectangleF(0, 1, Size, Size), format);
        }

        // Icon.FromHandle does not own the handle; keep it and destroy it on dispose.
        var handle = bitmap.GetHicon();
        _handles.Add(handle);
        icon = Icon.FromHandle(handle);
        _icons[color] = icon;
        return icon;
    }

    public void Dispose()
    {
        foreach (var icon in _icons.Values) icon.Dispose();
        foreach (var handle in _handles) DestroyIcon(handle);
        _icons.Clear();
        _handles.Clear();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
