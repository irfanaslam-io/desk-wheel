using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Wheel.App.Platform;
using Wheel.Core.Files;

namespace Wheel.App;

/// <summary>
/// "Open these with Desk Wheel?": the kinds of files Desk Wheel can open, ticked, with what each opens in.
/// Mac: applied straight away (macOS asks nothing more). Windows: only Settings may change default apps, so it
/// opens Desk Wheel's page there. Shown once after install or update, and from Settings any time.
/// </summary>
internal sealed class DefaultAppsWindow : Window
{
    private readonly IPlatform _platform;
    private readonly Dictionary<FileTypeGroup, CheckBox> _boxes = [];
    private readonly Dictionary<FileTypeGroup, TextBlock> _states = [];
    private readonly TextBlock _result = Ui.Caption("");

    public DefaultAppsWindow(IPlatform platform)
    {
        _platform = platform;
        Ui.Window(this, "Open files with Desk Wheel");
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var intro = Ui.Body(platform.CanSetDefaultApps
            ? "Double-click a file and it opens in Desk Wheel. Choose which kinds; you can change this any time in Settings."
            : "Double-click a file and it opens in Desk Wheel. Windows lets only you change default apps, in Settings: " +
              "click the button below, then \"Set default\" at the top of Desk Wheel's page (Windows 11), or choose Desk Wheel for each file type.");

        var list = new StackPanel { Spacing = 2 };
        foreach (var group in FileTypes.Groups)
        {
            var box = new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Center, IsVisible = platform.CanSetDefaultApps };
            var state = Ui.Caption("", wrap: false);
            state.HorizontalAlignment = HorizontalAlignment.Right;
            state.VerticalAlignment = VerticalAlignment.Center;
            _boxes[group] = box;
            _states[group] = state;
            var text = new StackPanel
            {
                Spacing = 2, VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = group.Name, FontWeight = FontWeight.Medium, Foreground = Ui.Text },
                    Ui.Caption($"{group.Examples()} · open in {group.OpensIn}", wrap: false),
                },
            };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 6) };
            row.Children.Add(box);
            Grid.SetColumn(text, 1);
            text.Margin = new Thickness(platform.CanSetDefaultApps ? 8 : 0, 0, 0, 0);
            row.Children.Add(text);
            Grid.SetColumn(state, 2);
            row.Children.Add(state);
            list.Children.Add(row);
        }

        var later = Ui.Ghost("Not now", Close);
        var go = platform.CanSetDefaultApps
            ? Ui.Primary("Use Desk Wheel for these", Apply, IconKind.Check)
            : Ui.Primary("Open Windows Settings", () => { _platform.OpenDefaultAppsSettings(); Close(); }, IconKind.Settings);

        Content = new StackPanel
        {
            Margin = new Thickness(24, 20), Spacing = 14,
            Children =
            {
                Ui.Title("Open files with Desk Wheel"),
                intro,
                Ui.Card(list, new Thickness(16, 6)),
                _result,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { later, go } },
            },
        };
        Refresh();
    }

    private void Refresh()
    {
        if (!_platform.CanSetDefaultApps) return;
        foreach (var (group, state) in _states)
        {
            var already = _platform.IsDefaultApp(group.MacTypes);
            state.Text = already ? "Already Desk Wheel" : "";
            state.Foreground = already ? Ui.SuccessText : Ui.Muted;
        }
    }

    private void Apply()
    {
        var chosen = _boxes.Where(b => b.Value.IsChecked == true).Select(b => b.Key).ToList();
        if (chosen.Count == 0)
        {
            Close();
            return;
        }
        var problems = chosen.Select(g => _platform.SetDefaultApp(g.MacTypes)).OfType<string>().ToList();
        Refresh();
        if (problems.Count == 0)
        {
            Log.Trace($"Default app for: {string.Join(", ", chosen.Select(g => g.Id))}");
            Close();
            return;
        }
        _result.Text = string.Join("\n", problems);
        _result.Foreground = Ui.DangerText;
    }
}
