using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Wheel.Core.Files;

namespace Wheel.App;

/// <summary>
/// Bulk rename with a live preview: a name pattern ({name}, {n}, {date}, {folder}), find and replace, and
/// letter case. Nothing changes until Rename is clicked, nothing is ever overwritten, and Undo puts the
/// old names back.
/// </summary>
internal sealed class RenameWindow : Window
{
    private readonly IReadOnlyList<string> _paths;
    private readonly Action<string, string> _notify;
    private readonly TextBox _pattern = new() { Text = "{name}" };
    private readonly TextBox _find = new() { Watermark = "Find" };
    private readonly TextBox _replace = new() { Watermark = "Replace with" };
    private readonly ComboBox _case = new() { ItemsSource = new[] { "Keep letter case", "lowercase", "UPPERCASE", "Title Case" }, SelectedIndex = 0 };
    private readonly NumericUpDown _start = new() { Value = 1, Minimum = 0, Maximum = 1_000_000, Increment = 1, FormatString = "0", Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _preview = new() { Spacing = 2 };
    private readonly TextBlock _summary = new() { Foreground = Ui.Muted, FontSize = Ui.CaptionSize, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _apply;
    private readonly Button _undo;
    private IReadOnlyList<RenameItem> _plan = Array.Empty<RenameItem>();
    private IReadOnlyList<string> _current;
    private IReadOnlyList<(string From, string To)> _lastDone = Array.Empty<(string, string)>();

    public RenameWindow(IReadOnlyList<string> paths, Action<string, string> notify)
    {
        _paths = paths;
        _current = paths;
        _notify = notify;
        Ui.Window(this, $"Rename {paths.Count} item{(paths.Count == 1 ? "" : "s")}");
        Width = 760;
        Height = 660;
        MinWidth = 560;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _apply = Ui.Primary("Rename", Apply, IconKind.Check);
        _undo = Ui.Ghost("Undo", Undo, IconKind.Undo, "Put the previous names back");
        _undo.IsVisible = false;

        var tokens = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var (token, tip) in new[] { ("{name}", "The current name"), ("{n}", "1, 2, 3… (padded: 01, 02…)"), ("{date}", "The file's date, 2026-11-24"), ("{folder}", "The folder's name") })
        {
            var b = Ui.Ghost(token, () => { }, tip: tip);
            b.Padding = new Thickness(8, 3);
            b.FontSize = 12;
            b.BorderBrush = Ui.Line;
            b.BorderThickness = new Thickness(1);
            b.Click += (_, _) =>
            {
                var at = _pattern.CaretIndex;
                _pattern.Text = (_pattern.Text ?? "").Insert(Math.Clamp(at, 0, (_pattern.Text ?? "").Length), token);
                _pattern.CaretIndex = at + token.Length;
                _pattern.Focus();
            };
            tokens.Children.Add(b);
        }
        var presets = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var (label, pattern) in new[] { ("Number them", "{folder} {n}"), ("Date first", "{date} {name}"), ("Keep names", "{name}") })
        {
            var b = Ui.Secondary(label, () => _pattern.Text = pattern);
            b.Padding = new Thickness(10, 4);
            b.FontSize = 12;
            presets.Children.Add(b);
        }

        var form = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto"), Margin = new Thickness(4, 8) };
        AddRow(form, 0, "New name", _pattern);
        AddRow(form, 1, "Insert", tokens);
        AddRow(form, 2, "Presets", presets);
        AddRow(form, 3, "Find and replace", new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*"), Children = { _find, Col(_replace, 2) } });
        AddRow(form, 4, "Letter case", _case);
        AddRow(form, 5, "Start numbers at", _start);
        _case.MinWidth = 200;

        var close = Ui.Secondary("Close", Close);
        var footer = new DockPanel { Margin = new Thickness(20, 12, 20, 16) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _undo, close, _apply } };
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(_summary);

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,28,*"), Margin = new Thickness(12, 10, 12, 6) };
        columns.Children.Add(Ui.Label("Current name"));
        columns.Children.Add(Col(Ui.Label("New name"), 2));
        var previewCard = new Border
        {
            Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = Ui.CardRadius,
            Child = new DockPanel
            {
                Children =
                {
                    Docked(new StackPanel { Children = { columns, Ui.Divider() } }, Dock.Top),
                    new ScrollViewer { Content = _preview, Padding = new Thickness(12, 6) },
                },
            },
        };

        var top = new StackPanel
        {
            Margin = new Thickness(20, 16, 20, 8), Spacing = 10,
            Children =
            {
                Ui.Title(Title!),
                Ui.Caption("The preview updates as you type. Nothing changes until you click Rename, nothing is ever overwritten, and Undo puts the old names back."),
                Ui.Card(form, new Thickness(16, 8)),
            },
        };
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(footer);
        previewCard.Margin = new Thickness(20, 4, 20, 0);
        root.Children.Add(previewCard);
        Content = root;

        foreach (var box in new[] { _pattern, _find, _replace }) box.TextChanged += (_, _) => Refresh();
        _case.SelectionChanged += (_, _) => Refresh();
        _start.ValueChanged += (_, _) => Refresh();
        Opened += (_, _) => _pattern.Focus();
        Refresh();
    }

    private static Control Docked(Control c, Dock dock)
    {
        DockPanel.SetDock(c, dock);
        return c;
    }

    private static Control Col(Control c, int column)
    {
        Grid.SetColumn(c, column);
        return c;
    }

    private static void AddRow(Grid grid, int row, string label, Control control)
    {
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Muted };
        Grid.SetRow(text, row);
        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);
        control.Margin = new Thickness(0, 4);
        grid.Children.Add(text);
        grid.Children.Add(control);
    }

    private RenameRule Rule() => new(
        Pattern: string.IsNullOrWhiteSpace(_pattern.Text) ? "{name}" : _pattern.Text!,
        Find: _find.Text ?? "", Replace: _replace.Text ?? "",
        Case: (NameCase)Math.Max(0, _case.SelectedIndex),
        Start: (int)(_start.Value ?? 1));

    private void Refresh()
    {
        _plan = RenamePlan.Build(_current, Rule());
        _preview.Children.Clear();
        foreach (var item in _plan.Take(500))
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,28,*"), Margin = new Thickness(0, 3) };
            row.Children.Add(new TextBlock { Text = Path.GetFileName(item.From), TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Ui.Muted });
            row.Children.Add(Col(new TextBlock { Text = "→", Foreground = Ui.Faint, HorizontalAlignment = HorizontalAlignment.Center }, 1));
            row.Children.Add(Col(new TextBlock
            {
                Text = item.Problem ?? Path.GetFileName(item.To), TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = item.Problem is not null ? Ui.DangerText
                    : item.Changes ? Ui.Text : Ui.Faint,
            }, 2));
            _preview.Children.Add(row);
        }
        var changes = _plan.Count(i => i.Changes);
        var problems = _plan.Count(i => i.Problem is not null);
        _summary.Text = $"{changes} will be renamed" + (problems > 0 ? $" · {problems} can't be (shown in red)" : "");
        _apply.IsEnabled = changes > 0;
    }

    private void Apply()
    {
        var (done, failed) = RenamePlan.Apply(_plan);
        _lastDone = done;
        // Keep working on the files under their new names, so another change or Undo still applies.
        var renamed = done.ToDictionary(d => d.From, d => d.To);
        _current = _current.Select(p => renamed.TryGetValue(p.TrimEnd('/', '\\'), out var to) ? to : p).ToList();
        _undo.IsVisible = done.Count > 0;
        Refresh();
        _notify(failed.Count == 0 ? "Renamed" : "Renamed, with problems",
            failed.Count == 0 ? $"{done.Count} item{(done.Count == 1 ? "" : "s")} renamed" : $"{done.Count} renamed · {failed[0]}");
    }

    private void Undo()
    {
        var restored = RenamePlan.Undo(_lastDone);
        var back = _lastDone.ToDictionary(d => d.To, d => d.From);
        _current = _current.Select(p => back.TryGetValue(p, out var from) ? from : p).ToList();
        _lastDone = Array.Empty<(string, string)>();
        _undo.IsVisible = false;
        Refresh();
        _notify("Undone", $"{restored} name{(restored == 1 ? "" : "s")} put back");
    }
}
