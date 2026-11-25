using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Wheel.App;

/// <summary>A small "type something" window: a size, a password. Enter confirms, Esc cancels.</summary>
internal sealed class PromptWindow : Window
{
    private readonly TaskCompletionSource<string?> _answer = new();
    private readonly TextBox _input;
    private readonly TextBox? _confirm;
    private readonly TextBlock _problem = new() { Foreground = Ui.DangerText, FontSize = 12, IsVisible = false };
    private readonly Func<string, string?>? _check;

    private PromptWindow(string title, string message, string? initial, string? watermark, bool password, Func<string, string?>? check)
    {
        _check = check;
        Ui.Window(this, title);
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _input = new TextBox { Text = initial ?? "", Watermark = watermark, PasswordChar = password ? '•' : default };
        if (password)
            _confirm = new TextBox { Watermark = "Type it again", PasswordChar = '•' };

        var ok = Ui.Primary(password ? "Continue" : "OK", Confirm);
        ok.IsDefault = true;
        ok.MinWidth = 90;
        var cancel = Ui.Secondary("Cancel", Close);
        cancel.IsCancel = true;

        var panel = new StackPanel
        {
            Margin = new Thickness(22, 20, 22, 18), Spacing = 12,
            Children =
            {
                Ui.Heading(title),
                Ui.Caption(message),
                _input,
            },
        };
        if (_confirm is not null) panel.Children.Add(_confirm);
        panel.Children.Add(_problem);
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0),
            Children = { cancel, ok },
        });
        Content = panel;
        Opened += (_, _) => { _input.Focus(); _input.SelectAll(); };
        Closed += (_, _) => _answer.TrySetResult(null);
    }

    private void Confirm()
    {
        var text = _input.Text ?? "";
        string? problem = null;
        if (_confirm is not null && _confirm.Text != text) problem = "The two don't match.";
        else if (_check is not null) problem = _check(text);
        if (problem is not null)
        {
            _problem.Text = problem;
            _problem.IsVisible = true;
            return;
        }
        _answer.TrySetResult(text);
        Close();
    }

    /// <summary>Asks and waits. Null when cancelled.</summary>
    /// <param name="check">Returns a problem to show (and keep the window open), or null when the answer is fine.</param>
    public static Task<string?> AskAsync(string title, string message, string? initial = null, string? watermark = null,
                                         bool password = false, bool confirmPassword = false, Func<string, string?>? check = null)
    {
        var window = new PromptWindow(title, message, initial, watermark, password && confirmPassword, check);
        if (password && !confirmPassword) window._input.PasswordChar = '•';
        window.Show();
        window.Activate();
        return window._answer.Task;
    }
}
