using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace AutoRestartVoicemeeter.UI;

/// <summary>
/// Generates a stylised system-tray icon at runtime using GDI+ (System.Drawing).
/// The icon is a dark circle with a coloured accent ring and a "VM" label.
/// No .ico file required — the icon lives entirely in memory.
/// </summary>
public static class IconHelper
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// Creates a 32×32 <see cref="Icon"/> with the given accent colour.
    /// </summary>
    /// <param name="accent">Ring and text colour (e.g. green for "connected").</param>
    public static Icon Create(Color accent)
    {
        using var bmp = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g   = Graphics.FromImage(bmp);

        g.SmoothingMode      = SmoothingMode.AntiAlias;
        g.TextRenderingHint  = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.Transparent);

        // ── Subtle shadow/glow beneath the circle ─────────────────────────────
        using (var glow = new SolidBrush(Color.FromArgb(60, accent)))
            g.FillEllipse(glow, 3, 5, 27, 27);

        // ── Dark background circle ─────────────────────────────────────────────
        using (var bg = new LinearGradientBrush(
                   new Rectangle(0, 0, 32, 32),
                   Color.FromArgb(48, 48, 65),
                   Color.FromArgb(22, 22, 35),
                   LinearGradientMode.ForwardDiagonal))
        {
            g.FillEllipse(bg, 1, 1, 30, 30);
        }

        // ── Accent ring ────────────────────────────────────────────────────────
        using (var ring = new Pen(accent, 2.2f))
            g.DrawEllipse(ring, 2, 2, 28, 28);

        // ── "VM" label centred in the circle ──────────────────────────────────
        using var font   = new Font("Segoe UI", 8.5f, FontStyle.Bold, GraphicsUnit.Point);
        using var brush  = new SolidBrush(accent);
        var fmt = new StringFormat
        {
            Alignment     = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        g.DrawString("VM", font, brush, new RectangleF(1, 2, 30, 28), fmt);

        // ── Convert bitmap → managed Icon (destroy GDI handle immediately) ────
        var hIcon = bmp.GetHicon();
        var icon  = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }

    // ── Pre-defined colour palette ─────────────────────────────────────────────
    /// <summary>Green — VoiceMeeter connected and monitoring.</summary>
    public static Icon Connected   => Create(Color.FromArgb(0x4A, 0xDE, 0x80));
    /// <summary>Amber — engine restart in progress.</summary>
    public static Icon Restarting  => Create(Color.FromArgb(0xFB, 0xBF, 0x24));
    /// <summary>Red — error / VoiceMeeter not responding.</summary>
    public static Icon Error       => Create(Color.FromArgb(0xEF, 0x44, 0x44));
    /// <summary>Blue — application starting up.</summary>
    public static Icon Starting    => Create(Color.FromArgb(0x60, 0xA5, 0xFA));
}
