using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Lantern.App;

public partial class MainWindow
{
    private bool formattingVisible, composerExpanded;
    private readonly Dictionary<string, Button> formatControls = [];

    private void RestoreComposerDraft(string id)
    {
        replaceEmoticons = true;
        try { if (drafts.TryGetValue(id, out var draft)) Composer.Load(draft); else Composer.Clear(); }
        finally { replaceEmoticons = false; }
        if (drafts.GetValueOrDefault(id)?.HasFormatting == true) formattingVisible = true;
        UpdateComposerHeight();
    }
    private void FormatClick(object sender, RoutedEventArgs e)
    {
        if (!Composer.IsEnabled) return;
        formattingVisible = !formattingVisible;
        if (!formattingVisible) composerExpanded = false;
        UpdateComposerHeight(); Composer.Focus();
    }
    private void ExpandComposerClick(object sender, RoutedEventArgs e)
    {
        if (!Composer.IsEnabled) return;
        composerExpanded = !composerExpanded;
        UpdateComposerHeight(); Composer.Focus();
    }
    private void UpdateComposerHeight()
    {
        if (Composer is null || FormattingToolbar is null) return;
        FormattingToolbar.Visibility = formattingVisible ? Visibility.Visible : Visibility.Collapsed;
        FormatButton.SetResourceReference(Control.BackgroundProperty, formattingVisible ? "AccentSoftBrush" : "SurfaceBrush");
        Composer.Height = composerExpanded ? Math.Clamp(((ActualHeight > 0 ? ActualHeight : Height) - 340) * .65, 100, 480) : formattingVisible ? 112 : double.NaN;
        Composer.MaxHeight = composerExpanded ? Math.Max(130, Composer.Height) : 130;
        ExpandComposerIcon.Text = composerExpanded ? "\uE73F" : "\uE740";
        var label = composerExpanded ? "Thu gọn vùng soạn thảo" : "Mở rộng vùng soạn thảo";
        ExpandComposerButton.ToolTip = label; AutomationProperties.SetName(ExpandComposerButton, label);
    }
    private void InitializeFormattingToolbar()
    {
        Button Add(string id, string text, string label, Action action)
        {
            var button = new Button { Content = text, ToolTip = label, Focusable = false, Padding = new Thickness(5, 2, 5, 2), MinHeight = 30, MinWidth = 30, Margin = new Thickness(1), BorderThickness = new Thickness(0), FontSize = 14 };
            AutomationProperties.SetName(button, label);
            button.Click += (_, _) => { if (!Composer.IsEnabled) return; action(); Composer.Focus(); ComposerSelectionChanged(Composer, new RoutedEventArgs()); };
            FormattingToolbar.Children.Add(button); formatControls[id] = button; return button;
        }
        void Execute(RoutedCommand command) { if (command.CanExecute(null, Composer)) command.Execute(null, Composer); }
        Add("bold", "B", "In đậm (Ctrl+B)", () => Execute(EditingCommands.ToggleBold)).FontWeight = FontWeights.Bold;
        Add("italic", "I", "In nghiêng (Ctrl+I)", () => Execute(EditingCommands.ToggleItalic)).FontStyle = FontStyles.Italic;
        Add("underline", "U̲", "Gạch chân (Ctrl+U)", () => Execute(EditingCommands.ToggleUnderline));
        Add("strike", "S̶", "Gạch ngang", () =>
        {
            var current = Composer.Selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
            var next = current?.Clone() ?? new();
            if (next.Any(d => d.Location == TextDecorationLocation.Strikethrough))
                foreach (var item in next.Where(d => d.Location == TextDecorationLocation.Strikethrough).ToArray()) next.Remove(item);
            else next.Add(TextDecorations.Strikethrough);
            Composer.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, next);
        });
        var sizes = Add("size", "Aa", "Cỡ chữ", () => OpenFormatMenu("size")); sizes.ContextMenu = new ContextMenu();
        foreach (var (name, size) in new[] { ("Nhỏ", 12), ("Trung bình", 14), ("Lớn", 18), ("Rất lớn", 24) })
        {
            var item = new MenuItem { Header = name }; item.Click += (_, _) => { Composer.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, (double)size); Composer.Focus(); }; sizes.ContextMenu.Items.Add(item);
        }
        var colors = Add("color", "A̲", "Màu chữ", () => OpenFormatMenu("color")); colors.ContextMenu = new ContextMenu();
        foreach (var (name, color) in new[] { ("Tự động", ""), ("Đỏ", "#D64242"), ("Cam", "#D87816"), ("Xanh lá", "#239B64"), ("Xanh dương", "#408BE5"), ("Tím", "#A46CE0") })
        {
            var item = new MenuItem { Header = name };
            item.Click += (_, _) => { Composer.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, color == "" ? Composer.Foreground : new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))); Composer.Focus(); }; colors.ContextMenu.Items.Add(item);
        }
        Add("clear", "⌫", "Xóa định dạng chữ", () => { Composer.Selection.ClearAllProperties(); });
        Add("bullets", "•≡", "Danh sách dấu chấm", () => Execute(EditingCommands.ToggleBullets));
        Add("numbers", "1≡", "Danh sách đánh số", () => Execute(EditingCommands.ToggleNumbering));
        Add("outdent", "⇤", "Giảm thụt lề", () => ChangeIndent(-1));
        Add("indent", "⇥", "Tăng thụt lề", () => ChangeIndent(1));
        Add("undo", "↶", "Hoàn tác (Ctrl+Z)", () => Execute(ApplicationCommands.Undo));
        Add("redo", "↷", "Làm lại (Ctrl+Y)", () => Execute(ApplicationCommands.Redo));
        ComposerSelectionChanged(Composer, new RoutedEventArgs());
    }
    private void OpenFormatMenu(string key)
    {
        var control = formatControls[key]; control.ContextMenu.PlacementTarget = control; control.ContextMenu.IsOpen = true;
    }
    private void ChangeIndent(int delta)
    {
        Composer.BeginChange();
        try
        {
            foreach (var p in RichDocuments.Paragraphs(Composer.Document.Blocks).Where(p => p.ContentEnd.CompareTo(Composer.Selection.Start) >= 0 && p.ContentStart.CompareTo(Composer.Selection.End) <= 0))
                p.Margin = new Thickness(Math.Clamp(p.Margin.Left + delta * 24, 0, 96), 0, 0, 4);
        }
        finally { Composer.EndChange(); }
    }
    private void ComposerSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (Composer is null || formatControls.Count == 0) return;
        var selection = Composer.Selection;
        var decorations = selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
        foreach (var (key, active) in new[] {
            ("bold", Equals(selection.GetPropertyValue(TextElement.FontWeightProperty), FontWeights.Bold)),
            ("italic", Equals(selection.GetPropertyValue(TextElement.FontStyleProperty), FontStyles.Italic)),
            ("underline", decorations?.Any(d => d.Location == TextDecorationLocation.Underline) == true),
            ("strike", decorations?.Any(d => d.Location == TextDecorationLocation.Strikethrough) == true) })
            formatControls[key].SetResourceReference(Control.BackgroundProperty, active ? "AccentSoftBrush" : "SurfaceBrush");
        formatControls["undo"].IsEnabled = Composer.CanUndo;
        formatControls["redo"].IsEnabled = Composer.CanRedo;
    }
}
