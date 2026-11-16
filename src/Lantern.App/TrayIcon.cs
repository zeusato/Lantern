using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Lantern.App;

// NotifyIcon handles Explorer restarts. All operations run on the WPF dispatcher thread.
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon notification;
    private readonly ContextMenuStrip menu;
    private readonly Icon original;
    private Icon? badge;
    private int lastCount = -1;
    private bool disposed;
    private string notificationConversation = "";

    public TrayIcon(Action open, Action lockApp, Action exit, Action<string> openConversation)
    {
        using var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Lantern;component/Assets/Lantern.ico"))!.Stream;
        original = new Icon(resource, 32, 32);
        menu = new ContextMenuStrip();
        menu.Items.Add("Mở Lantern", null, (_, _) => open());
        menu.Items.Add("Khóa ứng dụng", null, (_, _) => lockApp());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Thoát Lantern", null, (_, _) => exit());
        notification = new NotifyIcon { Icon = original, Text = "Lantern", ContextMenuStrip = menu, Visible = true };
        notification.DoubleClick += (_, _) => open();
        notification.BalloonTipClicked += (_, _) => openConversation(notificationConversation);
    }
    public void ShowMessage(string conversationId, string title, string message)
    {
        if (disposed) return;
        notificationConversation = conversationId;
        notification.ShowBalloonTip(5000, title.Length > 63 ? title[..60] + "…" : title, message.Length > 240 ? message[..237] + "…" : message, ToolTipIcon.Info);
    }

    public void UpdateUnread(int count)
    {
        if (disposed) return;
        count = Math.Max(0, count);
        if (lastCount == count) return;
        var next = count == 0 ? null : CreateBadge(original, count);
        notification.Icon = next ?? original;
        notification.Text = count == 0 ? "Lantern · Không có tin chưa đọc" : $"Lantern · {count:N0} tin chưa đọc";
        var previous = badge; badge = next; previous?.Dispose(); lastCount = count;
    }

    internal static Icon CreateBadge(Icon original, int count)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.DrawIcon(original, new Rectangle(0, 0, 28, 28));
        using var red = new SolidBrush(Color.FromArgb(211, 43, 65));
        using var border = new Pen(Color.White, 1.2f);
        graphics.FillEllipse(red, 5, 5, 26, 26);
        graphics.DrawEllipse(border, 5, 5, 26, 26);
        var text = count > 99 ? "99+" : count.ToString(CultureInfo.InvariantCulture);
        using var font = new Font("Segoe UI", text.Length > 2 ? 10 : text.Length > 1 ? 14 : 18, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(text, font, Brushes.White, new RectangleF(5, 5, 26, 26), format);
        var handle = bitmap.GetHicon();
        try { using var borrowed = Icon.FromHandle(handle); return (Icon)borrowed.Clone(); }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        notification.Visible = false; notification.Dispose(); menu.Dispose(); badge?.Dispose(); original.Dispose();
    }
}
