using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Lantern.Core;

namespace Lantern.App;

// One retained drawing per cell. A move repaints only changed cells and highlights.
// Artwork is vector-based and local: no image downloads or board snapshots on wire.
internal sealed class GameBoard : FrameworkElement
{
    private readonly int size;
    private readonly bool ships, reversi;
    private readonly Action<int> click;
    private readonly VisualCollection visuals;
    private readonly DrawingVisual[] tiles;
    private int[] cells, fleet = [];
    private HashSet<int> winning = [], legal = [];
    private int last = -1, hover = -1, selectedShip;
    private bool playable, vertical;
    internal int CellPaintCount { get; private set; }
    internal DrawingVisual CellVisual(int cell) => tiles[cell];
    private static readonly DependencyProperty PaletteProperty = DependencyProperty.Register("Palette", typeof(Brush), typeof(GameBoard), new FrameworkPropertyMetadata(null, (_, _) => { }));
    public GameBoard(int size, int[] cells, int last, IReadOnlyCollection<int> winning, Action<int> click, bool playable, bool ships = false, bool reversi = false)
    {
        this.size = size; this.ships = ships; this.reversi = reversi; this.click = click;
        this.cells = new int[size * size]; visuals = new VisualCollection(this); tiles = Enumerable.Range(0, size * size).Select(_ => new DrawingVisual()).ToArray();
        foreach (var tile in tiles) visuals.Add(tile);
        ClipToBounds = true; SetResourceReference(PaletteProperty, "SurfaceBrush");
        Update(cells, last, winning, playable);
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e) { base.OnPropertyChanged(e); if (e.Property == PaletteProperty && tiles is not null) PaintAll(); }
    protected override int VisualChildrenCount => visuals.Count;
    protected override Visual GetVisualChild(int index) => visuals[index];
    protected override void OnRenderSizeChanged(SizeChangedInfo info) { base.OnRenderSizeChanged(info); PaintAll(); }
    internal static int[] PlacementCells(int cell, int length, bool vertical)
    {
        if (cell is < 0 or >= 100 || (vertical ? cell / 10 : cell % 10) + length > 10) return [];
        return Enumerable.Range(0, length).Select(i => cell + i * (vertical ? 10 : 1)).ToArray();
    }
    private int[] Preview() => !playable || hover < 0 || !ships ? [] : fleet.Length == 0 ? [hover] : PlacementCells(hover, GameFleet.Lengths[selectedShip - 1], vertical) is { Length: > 0 } values ? values : [hover];
    internal void Update(int[] next, int nextLast, IReadOnlyCollection<int> highlights, bool enabled, int[]? placement = null, int selectedShip = 0, bool vertical = false, IReadOnlyCollection<int>? legal = null)
    {
        var dirty = new HashSet<int>(); var nextWinning = highlights.ToHashSet(); var nextLegal = (legal ?? []).ToHashSet(); var nextFleet = placement ?? [];
        // Moving a hull also changes the bow/stern of adjacent, overlapping cells.
        for (int i = 0; i < cells.Length; i++)
            if ((fleet.Length > i ? fleet[i] : 0) != (nextFleet.Length > i ? nextFleet[i] : 0))
            {
                if (i % size > 0) dirty.Add(i - 1); if (i % size < size - 1) dirty.Add(i + 1);
                if (i >= size) dirty.Add(i - size); if (i + size < cells.Length) dirty.Add(i + size);
            }
        for (int i = 0; i < cells.Length; i++)
            if (cells[i] != next[i] || winning.Contains(i) != nextWinning.Contains(i) || this.legal.Contains(i) != nextLegal.Contains(i) || (i == last) != (i == nextLast) ||
                (fleet.Length > i ? fleet[i] : 0) != (nextFleet.Length > i ? nextFleet[i] : 0) ||
                (fleet.Length > i && fleet[i] > 0 && fleet[i] == this.selectedShip) != (nextFleet.Length > i && nextFleet[i] > 0 && nextFleet[i] == selectedShip)) dirty.Add(i);
        dirty.UnionWith(Preview());
        cells = next.ToArray(); fleet = nextFleet.ToArray(); last = nextLast; winning = nextWinning; this.legal = nextLegal; playable = enabled; this.selectedShip = selectedShip; this.vertical = vertical;
        Cursor = enabled ? fleet.Length > 0 ? Cursors.Hand : ships ? Cursors.Cross : Cursors.Hand : Cursors.Arrow;
        dirty.UnionWith(Preview()); foreach (var cell in dirty) Paint(cell);
    }
    private static SolidColorBrush Brush(string color) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return b; }
    private static readonly Brush SeaA = Brush("#163C52"), SeaB = Brush("#183F56"), SeaLine = Brush("#2B5369");
    private static readonly Brush FeltA = Brush("#146652"), FeltB = Brush("#166D58"), FeltLine = Brush("#257F68");
    private static readonly Brush WoodA = Brush("#EEDDC0"), WoodB = Brush("#EAD7B7"), WoodLine = Brush("#C8AF88");
    private void PaintAll() { if (ActualWidth <= 0) return; for (int i = 0; i < cells.Length; i++) Paint(i); }
    private void Paint(int i)
    {
        if (ActualWidth <= 0 || i < 0 || i >= tiles.Length) return;
        CellPaintCount++;
        double u = ActualWidth / size; var box = new Rect(i % size * u, i / size * u, u, u); var center = new Point(box.X + u / 2, box.Y + u / 2); int value = cells[i];
        using var dc = tiles[i].RenderOpen();
        bool alternate = (i / size + i % size) % 2 == 0;
        dc.DrawRectangle(ships ? alternate ? SeaA : SeaB : reversi ? alternate ? FeltA : FeltB : alternate ? WoodA : WoodB, new Pen(ships ? SeaLine : reversi ? FeltLine : WoodLine, .6), box);
        if (winning.Contains(i)) dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(110, 246, 192, 73)), null, box);
        if (ships)
        {
            // Riveted steel hulls, white wakes and warm impact markers.
            if (value == 3 || value == 5 && fleet.Length > i && fleet[i] > 0)
            {
                PaintHull(dc, i, box, u);
            }
            if (value == 4) { dc.DrawEllipse(null, new Pen(Brush("#5B8CA7"), 1), center, u * .21, u * .21); dc.DrawEllipse(Brush("#B3D7E8"), null, center, u * .06, u * .06); }
            if (value == 5)
            {
                dc.DrawEllipse(new RadialGradientBrush(Color.FromRgb(255, 209, 110), Color.FromRgb(222, 78, 54)), null, center, u * .3, u * .3);
                var pen = new Pen(Brush("#702E27"), Math.Max(1.5, u * .06)); dc.DrawLine(pen, new Point(center.X - u * .12, center.Y - u * .12), new Point(center.X + u * .12, center.Y + u * .12)); dc.DrawLine(pen, new Point(center.X + u * .12, center.Y - u * .12), new Point(center.X - u * .12, center.Y + u * .12));
            }
        }
        else if (reversi && value > 0)
        {
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(85, 0, 0, 0)), null, new Point(center.X + 1, center.Y + u * .06), u * .38, u * .38);
            var disk = value == 1 ? new RadialGradientBrush(Color.FromRgb(84, 98, 109), Color.FromRgb(13, 23, 31)) : new RadialGradientBrush(Colors.White, Color.FromRgb(198, 214, 217));
            disk.GradientOrigin = new Point(.3, .22);
            dc.DrawEllipse(disk, new Pen(value == 1 ? Brush("#07141B") : Brush("#F6FFFF"), 1), center, u * .36, u * .36);
        }
        else if (!reversi && !ships && value > 0)
        {
            var pen = new Pen(value == 1 ? Brush("#147F83") : Brush("#BA493B"), Math.Max(2, u * .09)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            double r = u * .26;
            if (value == 1) { dc.DrawLine(pen, new Point(center.X-r, center.Y-r), new Point(center.X+r, center.Y+r)); dc.DrawLine(pen, new Point(center.X+r, center.Y-r), new Point(center.X-r, center.Y+r)); }
            else dc.DrawEllipse(null, pen, center, r, r);
        }
        if (legal.Contains(i)) dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(130, 184, 245, 205)), new Pen(Brush("#B8F5CD"), 1), center, u * .1, u * .1);
        if (i == last) dc.DrawEllipse(ships || reversi ? Brushes.Gold : Brush("#8B622B"), null, new Point(box.Right - u * .12, box.Top + u * .12), Math.Max(1.5, u * .045), Math.Max(1.5, u * .045));
        if (Preview().Contains(i))
        {
            bool valid = fleet.Length == 0 ? value == 0 : PlacementCells(hover, GameFleet.Lengths[selectedShip - 1], vertical) is { Length: > 0 } preview && preview.All(c => fleet[c] == 0 || fleet[c] == selectedShip);
            dc.DrawRectangle(new SolidColorBrush(valid ? Color.FromArgb(80, 73, 224, 152) : Color.FromArgb(100, 245, 91, 91)), new Pen(valid ? Brush("#65EDAF") : Brush("#FF9292"), 2), new Rect(box.X + 1, box.Y + 1, u - 2, u - 2));
        }
    }
    private void PaintHull(DrawingContext dc, int cell, Rect box, double u)
    {
        int id = fleet.Length > cell ? fleet[cell] : 0;
        bool Same(int other) => id > 0 && other >= 0 && other < fleet.Length && fleet[other] == id;
        bool across = cell % size > 0 && Same(cell - 1) || cell % size < size - 1 && Same(cell + 1);
        bool before = across ? cell % size > 0 && Same(cell - 1) : Same(cell - size);
        bool after = across ? cell % size < size - 1 && Same(cell + 1) : Same(cell + size);
        var center = new Point(box.X + u / 2, box.Y + u / 2);
        if (!across) dc.PushTransform(new RotateTransform(90, center.X, center.Y));
        Point P(double x, double y) => new(box.X + x * u, box.Y + y * u);
        var shape = new StreamGeometry();
        using (var path = shape.Open())
        {
            path.BeginFigure(before ? P(0, .2) : P(.07, .5), true, true);
            if (!before) path.LineTo(P(.3, .2), true, false);
            path.LineTo(P(after ? 1 : .88, .2), true, false);
            if (after) path.LineTo(P(1, .8), true, false); else path.QuadraticBezierTo(P(1, .5), P(.88, .8), true, false);
            path.LineTo(before ? P(0, .8) : P(.3, .8), true, false);
        }
        shape.Freeze();
        dc.DrawGeometry(new LinearGradientBrush(Color.FromRgb(194, 218, 226), Color.FromRgb(78, 117, 138), 90), new Pen(id == selectedShip && id > 0 ? Brushes.Gold : Brush("#D1E6EE"), id == selectedShip && id > 0 ? 2 : .8), shape);
        dc.DrawLine(new Pen(Brush("#E6F1F4"), 1), P(before ? 0 : .3, .28), P(after ? 1 : .85, .28));
        if (before) { dc.DrawRoundedRectangle(Brush("#244B64"), new Pen(Brush("#A9C9D9"), .6), new Rect(box.X + u * .24, box.Y + u * .34, u * .5, u * .32), u * .04, u * .04); dc.DrawLine(new Pen(Brush("#8FBACF"), 1), P(.4, .37), P(.4, .62)); }
        if (!across) dc.Pop();
        if (!before && id > 0) Text(dc, id.ToString(), center, Math.Max(9, u * .34), Brush("#102D40"));
    }
    private void Text(DrawingContext dc, string value, Point center, double size, Brush brush)
    {
        var text = new FormattedText(value, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }
    private int CellAt(Point point) { if (ActualWidth <= 0 || point.X < 0 || point.Y < 0 || point.X >= ActualWidth || point.Y >= ActualHeight) return -1; return (int)(point.Y * size / ActualHeight) * size + (int)(point.X * size / ActualWidth); }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); int next = playable ? CellAt(e.GetPosition(this)) : -1; if (hover == next) return;
        var dirty = Preview().ToHashSet(); hover = next; dirty.UnionWith(Preview()); foreach (var cell in dirty) Paint(cell);
    }
    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); var dirty = Preview(); hover = -1; foreach (var cell in dirty) Paint(cell); }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e); int cell = CellAt(e.GetPosition(this));
        if (!playable || cell < 0 || ships && fleet.Length == 0 && cells[cell] != 0 || reversi && !legal.Contains(cell) || !ships && !reversi && cells[cell] != 0) return;
        click(cell); e.Handled = true;
    }
}
