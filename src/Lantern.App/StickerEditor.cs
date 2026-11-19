using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Lantern.App;

internal sealed class StickerEditor : StackPanel
{
    private readonly BitmapSource original;
    private BitmapSource working;
    private readonly Canvas canvas;
    private readonly Image image = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Image preview = new() { Width = 160, Height = 160, Stretch = Stretch.Uniform };
    private readonly Polyline selection = new() { Stroke = Brushes.DodgerBlue, StrokeThickness = 2, IsHitTestVisible = false };
    private readonly ComboBox mode = new() { ItemsSource = new[] { "Vùng chữ nhật", "Vẽ tự do quanh chủ thể" }, SelectedIndex = 0, MinWidth = 200 };
    private readonly Slider border = new() { Minimum = 0, Maximum = 16, Value = 6, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 180 };
    private readonly Slider tolerance = new() { Minimum = 5, Maximum = 100, Value = 30, Width = 150 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly List<Point> points = [];
    private Point start;
    private bool drawing;
    internal StickerEditor(BitmapSource source)
    {
        SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextBrush");
        original = working = StickerImaging.Fit(source);
        Children.Add(new TextBlock { Text = "Chế tác sticker", FontSize = 23, FontWeight = FontWeights.SemiBold });
        Children.Add(new TextBlock { Text = "Kéo vùng chọn hoặc vẽ kín quanh chủ thể rồi bấm Cắt vùng chọn. Ảnh xuất là PNG trong suốt.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) });
        var tools = new WrapPanel(); tools.Children.Add(mode);
        Button Button(string text, Action action) { var b = new Button { Content = text, Margin = new Thickness(4) }; b.Click += (_, _) => { try { action(); } catch (Exception ex) { status.Text = ex.Message; } }; return b; }
        tools.Children.Add(Button("Cắt vùng chọn", () => { working = StickerImaging.Select(working, points); ClearSelection(); Refresh(); }));
        tools.Children.Add(Button("Khôi phục ảnh", () => { working = original; ClearSelection(); Refresh(); })); Children.Add(tools);
        var body = new WrapPanel { Margin = new Thickness(0, 10, 0, 8) };
        canvas = new Canvas { Width = working.PixelWidth, Height = working.PixelHeight, Background = Checker(), ClipToBounds = true, Cursor = Cursors.Cross };
        image.Width = working.PixelWidth; image.Height = working.PixelHeight; canvas.Children.Add(image); canvas.Children.Add(selection);
        body.Children.Add(new Border { Child = canvas, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top });
        var side = new StackPanel { Width = 190 }; side.Children.Add(new TextBlock { Text = "Xem trước", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        side.Children.Add(new Border { Child = preview, Background = Checker() });
        side.Children.Add(new TextBlock { Text = "Độ dày viền trắng · 0–16 px", Margin = new Thickness(0, 12, 0, 4) }); side.Children.Add(border);
        body.Children.Add(side); Children.Add(body);
        var background = new WrapPanel(); background.Children.Add(Button("Xóa nền đơn sắc", () => { working = StickerImaging.RemoveEdgeBackground(working, (int)tolerance.Value); ClearSelection(); Refresh(); }));
        background.Children.Add(new TextBlock { Text = "Dung sai", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8) }); background.Children.Add(tolerance); Children.Add(background);
        Children.Add(new TextBlock { Text = "Xóa nền hoạt động tốt với nền đồng màu. Nền phức tạp: dùng vùng chọn tự do để giữ đúng chủ thể.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) }); Children.Add(status);
        System.Windows.Automation.AutomationProperties.SetName(mode, "Kiểu vùng chọn"); System.Windows.Automation.AutomationProperties.SetName(border, "Độ dày viền trắng"); System.Windows.Automation.AutomationProperties.SetName(tolerance, "Dung sai xóa nền");
        canvas.MouseLeftButtonDown += (_, e) => { start = Clamp(e.GetPosition(canvas)); points.Clear(); points.Add(start); drawing = true; canvas.CaptureMouse(); e.Handled = true; };
        canvas.MouseMove += (_, e) => { if (drawing) UpdateSelection(Clamp(e.GetPosition(canvas))); };
        canvas.MouseLeftButtonUp += (_, e) => { if (!drawing) return; UpdateSelection(Clamp(e.GetPosition(canvas))); drawing = false; canvas.ReleaseMouseCapture(); if (points.Count > 2) selection.Points.Add(points[0]); };
        canvas.LostMouseCapture += (_, _) => drawing = false;
        border.ValueChanged += (_, _) => Refresh(); mode.SelectionChanged += (_, _) => ClearSelection(); Refresh();
    }
    private Point Clamp(Point p) => new(Math.Clamp(p.X, 0, working.PixelWidth), Math.Clamp(p.Y, 0, working.PixelHeight));
    private void UpdateSelection(Point p)
    {
        if (mode.SelectedIndex == 0) { points.Clear(); points.AddRange([start, new Point(p.X, start.Y), p, new Point(start.X, p.Y)]); }
        else if ((points[^1] - p).Length >= 2 && points.Count < 4000) points.Add(p);
        selection.Points = new PointCollection(points); if (mode.SelectedIndex == 0) selection.Points.Add(start);
    }
    private void ClearSelection() { points.Clear(); selection.Points.Clear(); }
    private void Refresh()
    {
        image.Source = working;
        try { preview.Source = StickerImaging.OutlineAndTrim(working, (int)border.Value); status.Text = ""; }
        catch (Exception ex) { preview.Source = null; status.Text = ex.Message; }
    }
    internal byte[] Export() => StickerImaging.Png(StickerImaging.OutlineAndTrim(working, (int)border.Value));
    internal static Brush Checker()
    {
        var group = new DrawingGroup(); group.Children.Add(new GeometryDrawing(Brushes.WhiteSmoke, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        group.Children.Add(new GeometryDrawing(Brushes.LightGray, null, new RectangleGeometry(new Rect(0, 0, 8, 8)))); group.Children.Add(new GeometryDrawing(Brushes.LightGray, null, new RectangleGeometry(new Rect(8, 8, 8, 8))));
        return new DrawingBrush(group) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 16, 16), Stretch = Stretch.None };
    }
}
