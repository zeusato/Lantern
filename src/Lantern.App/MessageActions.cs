using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Lantern.App;

internal sealed class MessageActions : Border
{
    internal MessageActions(FrameworkElement host, Action reply)
    {
        Name = "MessageActions"; Visibility = Visibility.Collapsed; CornerRadius = new CornerRadius(12); Padding = new Thickness(3); BorderThickness = new Thickness(1);
        SetResourceReference(BackgroundProperty, "SurfaceBrush"); SetResourceReference(BorderBrushProperty, "LineBrush");
        Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.13 };
        var row = new StackPanel { Orientation = Orientation.Horizontal }; Child = row;
        var arrow = new Path { Data = Geometry.Parse("M 10,5 L 3,11 L 10,17 M 4,11 L 14,11 Q 21,11 21,19"), StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Width = 22, Height = 22 };
        arrow.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        var quote = RoundButton(arrow, "Trả lời có trích dẫn"); quote.Width = 30; quote.Height = 30; quote.Click += (_, _) => reply(); row.Children.Add(quote);
        host.MouseEnter += (_, _) => Visibility = Visibility.Visible;
        host.MouseLeave += (_, _) => UpdateVisibility(host);
        host.IsKeyboardFocusWithinChanged += (_, _) => UpdateVisibility(host);
        host.IsVisibleChanged += (_, _) => { if (!host.IsVisible) Visibility = Visibility.Collapsed; };
    }
    private void UpdateVisibility(FrameworkElement host) => Visibility = host.IsMouseOver || host.IsKeyboardFocusWithin ? Visibility.Visible : Visibility.Collapsed;
    internal static Button RoundButton(UIElement content, string label)
    {
        var button = new Button { Content = content, Height = 24, MinHeight = 0, Padding = new Thickness(4, 2, 4, 2), Margin = new Thickness(0), BorderThickness = new Thickness(1), ToolTip = label };
        button.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush"); button.SetResourceReference(Control.BorderBrushProperty, "LineBrush"); button.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        foreach (var (property, name) in new[] { (Border.BackgroundProperty, "Background"), (Border.BorderBrushProperty, "BorderBrush"), (Border.BorderThicknessProperty, "BorderThickness"), (Border.PaddingProperty, "Padding") }) border.SetBinding(property, new Binding(name) { RelativeSource = RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center); presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center); border.AppendChild(presenter);
        button.Template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        button.MouseEnter += (_, _) => button.Opacity = 0.82; button.MouseLeave += (_, _) => button.Opacity = 1;
        AutomationProperties.SetName(button, label); return button;
    }
}
