using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Wheel.App;

/// <summary>
/// Wheel's design system: one palette, one type scale, one set of buttons and icons, used by every window,
/// so the app looks and behaves the same everywhere. Dark, neutral surfaces; the brand orange is kept for
/// the one main action on a screen and for selection.
/// </summary>
internal static class Ui
{
    // ---------- colour ----------

    /// <summary>The colours of one theme. Wheel has a dark and a light one; "System" follows macOS / Windows.</summary>
    public sealed record Palette(
        Color Canvas, Color Surface, Color Raised, Color Hover, Color Line, Color LineStrong,
        Color Text, Color Muted, Color Faint,
        Color AccentHover, Color AccentPressed, Color AccentSoft, Color AccentText,
        Color SuccessText, Color SuccessSoft, Color DangerText, Color InfoSoft, Color InfoLine, Color NeutralSoft,
        Color Stage, Color WheelSegment, Color WheelStroke, Color WheelCentre, Color WheelText, Color WheelTextQuiet, Color Bubble);

    public static readonly Palette Dark = new(
        Canvas: Color.FromRgb(0x13, 0x14, 0x17), Surface: Color.FromRgb(0x1B, 0x1C, 0x20), Raised: Color.FromRgb(0x24, 0x26, 0x2B),
        Hover: Color.FromRgb(0x2C, 0x2E, 0x34), Line: Color.FromRgb(0x2E, 0x30, 0x37), LineStrong: Color.FromRgb(0x44, 0x47, 0x50),
        Text: Color.FromRgb(0xEC, 0xED, 0xEF), Muted: Color.FromRgb(0xA0, 0xA4, 0xAC), Faint: Color.FromRgb(0x6C, 0x70, 0x79),
        AccentHover: Color.FromRgb(0xF0, 0x74, 0x40), AccentPressed: Color.FromRgb(0xCC, 0x52, 0x20),
        AccentSoft: Color.FromArgb(0x33, 0xE8, 0x62, 0x2C), AccentText: Color.FromRgb(0xFF, 0x9A, 0x6E),
        SuccessText: Color.FromRgb(0x5F, 0xD3, 0x9A), SuccessSoft: Color.FromArgb(0x2A, 0x30, 0xA4, 0x6C),
        DangerText: Color.FromRgb(0xFF, 0x7B, 0x7B), InfoSoft: Color.FromArgb(0x14, 0x3E, 0x8B, 0xF7), InfoLine: Color.FromArgb(0x40, 0x3E, 0x8B, 0xF7),
        NeutralSoft: Color.FromArgb(0x26, 0xA0, 0xA4, 0xAC), Stage: Color.FromRgb(0x0E, 0x0F, 0x11),
        WheelSegment: Color.FromRgb(0x22, 0x23, 0x27), WheelStroke: Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF),
        WheelCentre: Color.FromArgb(0xF7, 0x17, 0x18, 0x1B), WheelText: Colors.White, WheelTextQuiet: Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF),
        Bubble: Color.FromArgb(0xE6, 0x1C, 0x1D, 0x21));

    public static readonly Palette Light = new(
        Canvas: Color.FromRgb(0xF4, 0xF5, 0xF7), Surface: Colors.White, Raised: Color.FromRgb(0xF0, 0xF1, 0xF4),
        Hover: Color.FromRgb(0xE6, 0xE8, 0xEC), Line: Color.FromRgb(0xDC, 0xDF, 0xE4), LineStrong: Color.FromRgb(0xC2, 0xC7, 0xCF),
        Text: Color.FromRgb(0x1A, 0x1C, 0x20), Muted: Color.FromRgb(0x5A, 0x60, 0x6A), Faint: Color.FromRgb(0x8C, 0x92, 0x9B),
        AccentHover: Color.FromRgb(0xD9, 0x56, 0x1F), AccentPressed: Color.FromRgb(0xC0, 0x4A, 0x18),
        AccentSoft: Color.FromArgb(0x24, 0xE8, 0x62, 0x2C), AccentText: Color.FromRgb(0xC2, 0x4A, 0x16),
        SuccessText: Color.FromRgb(0x1C, 0x7F, 0x4F), SuccessSoft: Color.FromArgb(0x24, 0x30, 0xA4, 0x6C),
        DangerText: Color.FromRgb(0xC6, 0x2B, 0x30), InfoSoft: Color.FromArgb(0x14, 0x3E, 0x8B, 0xF7), InfoLine: Color.FromArgb(0x50, 0x3E, 0x8B, 0xF7),
        NeutralSoft: Color.FromArgb(0x1E, 0x5A, 0x60, 0x6A), Stage: Color.FromRgb(0xE4, 0xE6, 0xEA),
        WheelSegment: Colors.White, WheelStroke: Color.FromArgb(0x26, 0x00, 0x00, 0x00),
        WheelCentre: Color.FromArgb(0xFA, 0xF4, 0xF5, 0xF7), WheelText: Color.FromRgb(0x1A, 0x1C, 0x20), WheelTextQuiet: Color.FromRgb(0x5A, 0x60, 0x6A),
        Bubble: Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF));

    public static readonly Color AccentColor = Color.FromRgb(0xE8, 0x62, 0x2C);       // Wheel orange, the same in both themes
    public static readonly Color SuccessColor = Color.FromRgb(0x30, 0xA4, 0x6C);
    public static readonly Color DangerColor = Color.FromRgb(0xE5, 0x48, 0x4D);
    public static readonly Color InfoColor = Color.FromRgb(0x3E, 0x8B, 0xF7);

    /// <summary>The theme in use. The brushes below are recoloured in place when it changes, so open windows follow.</summary>
    public static Palette Current { get; private set; } = Dark;
    public static bool IsLight => ReferenceEquals(Current, Light);

    public static readonly SolidColorBrush Canvas = new(Dark.Canvas);
    public static readonly SolidColorBrush Surface = new(Dark.Surface);
    public static readonly SolidColorBrush Raised = new(Dark.Raised);
    public static readonly SolidColorBrush Hover = new(Dark.Hover);
    public static readonly SolidColorBrush Line = new(Dark.Line);
    public static readonly SolidColorBrush LineStrong = new(Dark.LineStrong);
    public static readonly SolidColorBrush Text = new(Dark.Text);
    public static readonly SolidColorBrush Muted = new(Dark.Muted);
    public static readonly SolidColorBrush Faint = new(Dark.Faint);
    public static readonly SolidColorBrush Accent = new(AccentColor);
    public static readonly SolidColorBrush AccentHover = new(Dark.AccentHover);
    public static readonly SolidColorBrush AccentPressed = new(Dark.AccentPressed);
    public static readonly SolidColorBrush AccentSoft = new(Dark.AccentSoft);
    public static readonly SolidColorBrush AccentText = new(Dark.AccentText);
    public static readonly SolidColorBrush Success = new(SuccessColor);
    public static readonly SolidColorBrush SuccessText = new(Dark.SuccessText);
    public static readonly SolidColorBrush SuccessSoft = new(Dark.SuccessSoft);
    public static readonly SolidColorBrush Danger = new(DangerColor);
    public static readonly SolidColorBrush DangerText = new(Dark.DangerText);
    public static readonly SolidColorBrush Info = new(InfoColor);
    public static readonly SolidColorBrush InfoSoft = new(Dark.InfoSoft);
    public static readonly SolidColorBrush InfoLine = new(Dark.InfoLine);
    public static readonly SolidColorBrush NeutralSoft = new(Dark.NeutralSoft);
    public static readonly SolidColorBrush Stage = new(Dark.Stage);
    /// <summary>Cards that float over other apps (notifications, the screenshot card): nearly opaque surface.</summary>
    public static readonly SolidColorBrush Floating = new(WithAlpha(Dark.Surface, 0xF8));
    /// <summary>A row under the pointer, lighter than a selected one.</summary>
    public static readonly SolidColorBrush RowHover = new(WithAlpha(Dark.Raised, 0x80));
    public static readonly SolidColorBrush WheelSegment = new(Dark.WheelSegment);
    public static readonly SolidColorBrush WheelStroke = new(Dark.WheelStroke);
    public static readonly SolidColorBrush WheelCentre = new(Dark.WheelCentre);
    public static readonly SolidColorBrush WheelText = new(Dark.WheelText);
    public static readonly SolidColorBrush WheelTextQuiet = new(Dark.WheelTextQuiet);
    public static readonly SolidColorBrush Bubble = new(Dark.Bubble);

    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    /// <summary>"system", "light" or "dark", as stored in settings.</summary>
    public static string Mode { get; private set; } = "system";

    private static Application? _app;

    /// <summary>Switches theme. "system" follows the OS, and keeps following it when the person changes it there.</summary>
    public static void SetMode(string mode)
    {
        Mode = mode is "light" or "dark" ? mode : "system";
        if (_app is null) return;
        var light = Mode switch
        {
            "light" => true,
            "dark" => false,
            _ => _app.PlatformSettings?.GetColorValues().ThemeVariant == Avalonia.Platform.PlatformThemeVariant.Light,
        };
        Apply(light ? Light : Dark);
    }

    private static void Apply(Palette p)
    {
        Current = p;
        Canvas.Color = p.Canvas; Surface.Color = p.Surface; Raised.Color = p.Raised; Hover.Color = p.Hover;
        Line.Color = p.Line; LineStrong.Color = p.LineStrong; Text.Color = p.Text; Muted.Color = p.Muted; Faint.Color = p.Faint;
        AccentHover.Color = p.AccentHover; AccentPressed.Color = p.AccentPressed; AccentSoft.Color = p.AccentSoft; AccentText.Color = p.AccentText;
        SuccessText.Color = p.SuccessText; SuccessSoft.Color = p.SuccessSoft; DangerText.Color = p.DangerText;
        InfoSoft.Color = p.InfoSoft; InfoLine.Color = p.InfoLine; NeutralSoft.Color = p.NeutralSoft; Stage.Color = p.Stage;
        Floating.Color = WithAlpha(p.Surface, 0xF8); RowHover.Color = WithAlpha(p.Raised, ReferenceEquals(p, Light) ? (byte)0xFF : (byte)0x80);
        WheelSegment.Color = p.WheelSegment; WheelStroke.Color = p.WheelStroke; WheelCentre.Color = p.WheelCentre;
        WheelText.Color = p.WheelText; WheelTextQuiet.Color = p.WheelTextQuiet; Bubble.Color = p.Bubble;
        if (_app is not null)
            _app.RequestedThemeVariant = ReferenceEquals(p, Light) ? ThemeVariant.Light : ThemeVariant.Dark; // Fluent's own parts follow
    }

    // ---------- type and spacing ----------

    public const double TitleSize = 17, HeadingSize = 14, BodySize = 13, CaptionSize = 11.5, LabelSize = 11;
    public static readonly CornerRadius Radius = new(6), CardRadius = new(10);

    /// <summary>
    /// Installs the theme: Fluent dark, Wheel's accent for checkboxes, sliders, focus and selection, and the
    /// button variants (primary / ghost / danger / tool).
    /// </summary>
    public static void Install(Application app, string mode = "system")
    {
        _app = app;
        SetMode(mode);
        // Follow the OS when it switches between light and dark (only while the mode is "system").
        if (app.PlatformSettings is { } platform)
            platform.ColorValuesChanged += (_, _) => { if (Mode == "system") Avalonia.Threading.Dispatcher.UIThread.Post(() => SetMode("system")); };

        foreach (var key in new[] { "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorDark1" })
            app.Resources[key] = AccentColor;
        app.Resources["SystemAccentColorLight2"] = Color.FromRgb(0xF0, 0x74, 0x40);
        app.Resources["SystemAccentColorLight3"] = Color.FromRgb(0xF0, 0x74, 0x40);
        app.Resources["SystemAccentColorDark2"] = Color.FromRgb(0xCC, 0x52, 0x20);
        app.Resources["SystemAccentColorDark3"] = Color.FromRgb(0xCC, 0x52, 0x20);
        app.Resources["ControlCornerRadius"] = Radius;
        app.Resources["OverlayCornerRadius"] = CardRadius;
        // Inputs: a quiet raised field instead of the default near-black.
        app.Resources["TextControlBackground"] = Raised;
        app.Resources["TextControlBackgroundPointerOver"] = Hover;
        app.Resources["TextControlBackgroundFocused"] = Raised;
        app.Resources["TextControlBorderBrush"] = Line;
        app.Resources["TextControlBorderBrushPointerOver"] = LineStrong;
        app.Resources["TextControlBackgroundFocused"] = Surface;
        app.Resources["TextControlForeground"] = Text;
        app.Resources["TextControlPlaceholderForeground"] = Faint;
        app.Resources["ComboBoxForeground"] = Text;
        app.Resources["ComboBoxDropDownBackground"] = Surface;
        app.Resources["ComboBoxDropDownBorderBrush"] = Line;
        app.Resources["ToolTipForeground"] = Text;
        app.Resources["TextControlForegroundFocused"] = Text;
        app.Resources["ComboBoxBackground"] = Raised;
        app.Resources["ComboBoxBackgroundPointerOver"] = Hover;
        app.Resources["ComboBoxBorderBrush"] = Line;
        app.Resources["ButtonBackground"] = Raised;
        app.Resources["ButtonBackgroundPointerOver"] = Hover;
        app.Resources["ButtonBackgroundPressed"] = Line;
        app.Resources["ButtonBorderBrush"] = Line;
        app.Resources["ButtonBorderBrushPointerOver"] = LineStrong;
        app.Resources["ButtonForegroundPointerOver"] = Text;
        app.Resources["ButtonForegroundPressed"] = Text;
        app.Resources["ButtonForeground"] = Text;
        app.Resources["ToolTipBackground"] = Raised;
        app.Resources["ToolTipBorderBrush"] = Line;

        Variant(app, "primary", Accent, AccentHover, AccentPressed, Brushes.White, Brushes.Transparent);
        Variant(app, "danger", Danger, new SolidColorBrush(Color.FromRgb(0xEE, 0x5D, 0x62)), new SolidColorBrush(Color.FromRgb(0xC9, 0x3A, 0x3F)), Brushes.White, Brushes.Transparent);
        Variant(app, "ghost", Brushes.Transparent, Hover, Line, Muted, Brushes.Transparent, hoverForeground: Text);
        // Tool buttons (editor toolbar): quiet until chosen, then accent-tinted.
        Variant(app, "tool", Brushes.Transparent, Hover, Line, Muted, Brushes.Transparent, hoverForeground: Text);
        var chosen = new Style(x => x.OfType<ToggleButton>().Class("tool").Class(":checked").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));
        chosen.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, AccentSoft));
        chosen.Setters.Add(new Setter(ContentPresenter.ForegroundProperty, AccentText));
        chosen.Setters.Add(new Setter(ContentPresenter.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(0x80, 0xE8, 0x62, 0x2C))));
        app.Styles.Add(chosen);

        // Lists (settings sidebar and others): a quiet raised row when chosen, not a solid accent block.
        var item = new Style(x => x.OfType<ListBoxItem>());
        item.Setters.Add(new Setter(ListBoxItem.CornerRadiusProperty, Radius));
        item.Setters.Add(new Setter(ListBoxItem.MarginProperty, new Thickness(0, 1)));
        item.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(12, 8)));
        item.Setters.Add(new Setter(ListBoxItem.ForegroundProperty, Muted));
        app.Styles.Add(item);
        foreach (var (state, brush, fg) in new[]
                 {
                     (":pointerover", Raised, Text), (":selected", Hover, Text), (":selected:pointerover", Hover, Text),
                     (":selected:focus", Hover, Text), (":pressed", Hover, Text),
                 })
        {
            var s = new Style(x =>
            {
                var sel = x.OfType<ListBoxItem>();
                foreach (var pseudo in state.Split(':', StringSplitOptions.RemoveEmptyEntries)) sel = sel.Class(":" + pseudo);
                return sel.Template().OfType<ContentPresenter>().Name("PART_ContentPresenter");
            });
            s.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, brush));
            s.Setters.Add(new Setter(ContentPresenter.ForegroundProperty, fg));
            app.Styles.Add(s);
        }
    }

    /// <summary>A button look, applied to both Button and ToggleButton with the class.</summary>
    private static void Variant(Application app, string cls, IBrush background, IBrush hover, IBrush pressed, IBrush foreground,
                                IBrush border, IBrush? hoverForeground = null)
    {
        foreach (var type in new[] { typeof(Button), typeof(ToggleButton) })
        {
            var normal = new Style(x => x.OfType(type).Class(cls));
            normal.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, background));
            normal.Setters.Add(new Setter(TemplatedControl.ForegroundProperty, foreground));
            normal.Setters.Add(new Setter(TemplatedControl.BorderBrushProperty, border));
            app.Styles.Add(normal);

            // Disabled: no fill, faint text (Fluent's default grey block looks like a pressed button).
            var off = new Style(x => x.OfType(type).Class(cls).Class(":disabled").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));
            off.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, cls is "primary" or "danger" ? Raised : Brushes.Transparent));
            off.Setters.Add(new Setter(ContentPresenter.ForegroundProperty, Faint));
            off.Setters.Add(new Setter(ContentPresenter.BorderBrushProperty, Brushes.Transparent));
            app.Styles.Add(off);

            foreach (var (state, brush) in new[] { (":pointerover", hover), (":pressed", pressed) })
            {
                var s = new Style(x => x.OfType(type).Class(cls).Class(state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));
                s.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, brush));
                s.Setters.Add(new Setter(ContentPresenter.BorderBrushProperty, border));
                if (hoverForeground is not null || cls is "primary" or "danger")
                    s.Setters.Add(new Setter(ContentPresenter.ForegroundProperty, hoverForeground ?? foreground));
                app.Styles.Add(s);
            }
        }
    }

    /// <summary>Window defaults: canvas background, readable minimum size, the app title.</summary>
    public static void Window(Window window, string title)
    {
        window.Title = title;
        if (AppIcons.Window is { } icon) window.Icon = icon;
        window.Background = Canvas;
        window.Foreground = Text;
        window.FontSize = BodySize;
    }

    // ---------- text ----------

    public static TextBlock Title(string text) => new() { Text = text, FontSize = TitleSize, FontWeight = FontWeight.SemiBold, Foreground = Text };
    public static TextBlock Heading(string text) => new() { Text = text, FontSize = HeadingSize, FontWeight = FontWeight.SemiBold, Foreground = Text };
    public static TextBlock Body(string text, bool wrap = true) => new() { Text = text, FontSize = BodySize, Foreground = Text, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap };
    public static TextBlock Caption(string text, bool wrap = true) => new() { Text = text, FontSize = CaptionSize, Foreground = Muted, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap };

    /// <summary>Small upper-case label above a group ("SCREENSHOTS").</summary>
    public static TextBlock Label(string text) => new()
    {
        Text = text.ToUpperInvariant(), FontSize = LabelSize, FontWeight = FontWeight.SemiBold, Foreground = Faint, LetterSpacing = 0.6,
    };

    // ---------- buttons ----------

    private static Button MakeButton(string text, Action click, string? cls, IconKind? icon, string? tip)
    {
        var b = new Button { Padding = new Thickness(12, 6), CornerRadius = Radius, VerticalContentAlignment = VerticalAlignment.Center };
        if (cls is not null) b.Classes.Add(cls);
        b.Content = icon is { } i ? IconText(i, text) : text;
        if (tip is not null) ToolTip.SetTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>The one main action on a screen (orange).</summary>
    public static Button Primary(string text, Action click, IconKind? icon = null, string? tip = null) => MakeButton(text, click, "primary", icon, tip);

    /// <summary>Ordinary actions (raised grey).</summary>
    public static Button Secondary(string text, Action click, IconKind? icon = null, string? tip = null) => MakeButton(text, click, null, icon, tip);

    /// <summary>Low-emphasis actions on cards and rows (no fill until hovered).</summary>
    public static Button Ghost(string text, Action click, IconKind? icon = null, string? tip = null) => MakeButton(text, click, "ghost", icon, tip);

    public static Button Destructive(string text, Action click, IconKind? icon = null, string? tip = null) => MakeButton(text, click, "danger", icon, tip);

    /// <summary>A square icon-only button with a tooltip.</summary>
    public static Button IconButton(IconKind icon, string tip, Action click, double size = 16)
    {
        var b = new Button { Padding = new Thickness(6), CornerRadius = Radius, Content = Icon(icon, size) };
        b.Classes.Add("ghost");
        ToolTip.SetTip(b, tip);
        b.Click += (_, e) => { click(); e.Handled = true; };
        return b;
    }

    public static Control IconText(IconKind icon, string text, double size = 15) => new StackPanel
    {
        Orientation = Orientation.Horizontal, Spacing = 7,
        Children = { Icon(icon, size), new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } },
    };

    // ---------- containers ----------

    /// <summary>A bordered surface for a group of related settings or content.</summary>
    public static Border Card(Control child, Thickness? padding = null) => new()
    {
        Background = Surface, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = CardRadius,
        Padding = padding ?? new Thickness(16, 4), Child = child,
    };

    public static Border Divider() => new() { Height = 1, Background = Line };

    /// <summary>A text box with a search icon inside.</summary>
    public static TextBox SearchBox(string watermark)
    {
        var box = new TextBox { Watermark = watermark, Padding = new Thickness(32, 7, 10, 7) };
        box.InnerLeftContent = new ContentControl { Content = Icon(IconKind.Search, 14, Faint), Margin = new Thickness(10, 0, -24, 0), VerticalAlignment = VerticalAlignment.Center };
        box.Padding = new Thickness(30, 7, 10, 7);
        return box;
    }

    /// <summary>A window's top bar: title, an optional subtitle, and actions on the right.</summary>
    public static Control Header(string title, string? subtitle = null, params Control[] actions)
    {
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { Title(title) } };
        if (subtitle is not null) text.Children.Add(Caption(subtitle, wrap: false));
        var bar = new DockPanel { Margin = new Thickness(20, 16, 20, 14) };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        foreach (var a in actions) right.Children.Add(a);
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        bar.Children.Add(text);
        return new StackPanel { Children = { bar, Divider() } };
    }

    // ---------- icons ----------

    /// <summary>A 24×24 line icon drawn in the current text colour (or <paramref name="brush"/>).</summary>
    public static Control Icon(IconKind kind, double size = 16, IBrush? brush = null)
    {
        var (data, filled) = Icons.For(kind);
        var path = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(data),
            StrokeThickness = 1.8, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
        };
        if (brush is not null)
        {
            path.Stroke = brush;
            if (filled) path.Fill = brush;
        }
        else
        {
            // Follow the inherited text colour, so icons in buttons change with hover like the label does.
            path.Bind(Shape.StrokeProperty, path.GetObservable(Avalonia.Controls.Documents.TextElement.ForegroundProperty));
            if (filled) path.Bind(Shape.FillProperty, path.GetObservable(Avalonia.Controls.Documents.TextElement.ForegroundProperty));
        }
        return new Viewbox
        {
            Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center,
            Child = new Avalonia.Controls.Canvas { Width = 24, Height = 24, Children = { path } },
        };
    }
}

internal enum IconKind
{
    Arrow, Box, Highlight, Text, Steps, Blur, Redact, Crop, Sparkle, Undo, Redo, Copy, Save, Check, Close, Search,
    Pin, Star, Folder, Edit, Wheel, Settings, Trash, Clipboard, Info, Warning, Image, File, Refresh, Plus, Eye,
    Line, Ellipse, Move,
}

/// <summary>Wheel's own simple line icons, on a 24×24 grid.</summary>
internal static class Icons
{
    public static (string Data, bool Filled) For(IconKind kind) => kind switch
    {
        IconKind.Arrow => ("M5 19 L19 5 M10 5 L19 5 L19 14", false),
        IconKind.Box => ("M4 6 L20 6 L20 18 L4 18 Z", false),
        IconKind.Highlight => ("M6 14 L14 6 L18 10 L10 18 L6 18 Z M4 21 L20 21", false),
        IconKind.Text => ("M5 5 L19 5 M12 5 L12 20 M9 20 L15 20", false),
        IconKind.Steps => ("M12 3 A9 9 0 1 1 11.99 3 Z M10.5 9 L12.5 8 L12.5 16.5", false),
        IconKind.Blur => ("M5 5 L8 5 L8 8 L5 8 Z M11 5 L14 5 L14 8 L11 8 Z M17 5 L20 5 L20 8 L17 8 Z M8 11 L11 11 L11 14 L8 14 Z M14 11 L17 11 L17 14 L14 14 Z M5 17 L8 17 L8 20 L5 20 Z M11 17 L14 17 L14 20 L11 20 Z M17 17 L20 17 L20 20 L17 20 Z", false),
        IconKind.Redact => ("M3 9 L21 9 L21 15 L3 15 Z", true),
        IconKind.Crop => ("M7 3 L7 17 L21 17 M3 7 L17 7 L17 21", false),
        IconKind.Sparkle => ("M12 3 L13.6 9.4 L20 11 L13.6 12.6 L12 19 L10.4 12.6 L4 11 L10.4 9.4 Z M19 3 L19 6 M17.5 4.5 L20.5 4.5", false),
        IconKind.Undo => ("M9 14 L4 9 L9 4 M4 9 L15 9 A5.5 5.5 0 0 1 15 20 L11 20", false),
        IconKind.Redo => ("M15 14 L20 9 L15 4 M20 9 L9 9 A5.5 5.5 0 0 0 9 20 L13 20", false),
        IconKind.Copy => ("M9 9 L20 9 L20 20 L9 20 Z M5 15 L4 15 L4 4 L15 4 L15 5", false),
        IconKind.Save => ("M12 4 L12 15 M7 10 L12 15 L17 10 M5 20 L19 20", false),
        IconKind.Check => ("M5 12.5 L10 17.5 L19 7", false),
        IconKind.Close => ("M6 6 L18 18 M18 6 L6 18", false),
        IconKind.Search => ("M10.5 4 A6.5 6.5 0 1 1 10.49 4 Z M15.5 15.5 L20 20", false),
        IconKind.Pin => ("M12 16 L12 21 M8 3 L16 3 L15 9 L18 13 L6 13 L9 9 Z", false),
        IconKind.Star => ("M12 3.5 L14.5 9 L20.5 9.6 L16 13.6 L17.3 19.5 L12 16.5 L6.7 19.5 L8 13.6 L3.5 9.6 L9.5 9 Z", false),
        IconKind.Folder => ("M3 6 L9 6 L11 8 L21 8 L21 19 L3 19 Z", false),
        IconKind.Edit => ("M4 20 L8 19 L19 8 L16 5 L5 16 Z M14 7 L17 10", false),
        IconKind.Wheel => ("M12 3 A9 9 0 1 1 11.99 3 Z M12 8.5 A3.5 3.5 0 1 1 11.99 8.5 Z M12 3 L12 8.5 M12 15.5 L12 21 M3 12 L8.5 12 M15.5 12 L21 12", false),
        IconKind.Settings => ("M4 7 L20 7 M4 17 L20 17 M9 4.5 L9 9.5 M15 14.5 L15 19.5", false),
        IconKind.Trash => ("M5 7 L19 7 M10 7 L10 4 L14 4 L14 7 M7 7 L8 20 L16 20 L17 7", false),
        IconKind.Clipboard => ("M9 3.5 L15 3.5 L15 6.5 L9 6.5 Z M7 5 L5 5 L5 21 L19 21 L19 5 L17 5", false),
        IconKind.Info => ("M12 3 A9 9 0 1 1 11.99 3 Z M12 11 L12 16.5 M12 7.5 L12 8", false),
        IconKind.Warning => ("M12 3.5 L21.5 20 L2.5 20 Z M12 10 L12 14 M12 17 L12 17.5", false),
        IconKind.Image => ("M4 5 L20 5 L20 19 L4 19 Z M4 16 L9 11 L13 15 L16 12 L20 16 M15 8.5 L15 9", false),
        IconKind.File => ("M6 3 L14 3 L19 8 L19 21 L6 21 Z M14 3 L14 8 L19 8", false),
        IconKind.Refresh => ("M20 11 A8 8 0 1 0 18 16.5 M20 5 L20 11 L14 11", false),
        IconKind.Plus => ("M12 5 L12 19 M5 12 L19 12", false),
        IconKind.Eye => ("M2.5 12 C5 7 8.5 5 12 5 C15.5 5 19 7 21.5 12 C19 17 15.5 19 12 19 C8.5 19 5 17 2.5 12 Z M12 9 A3 3 0 1 1 11.99 9 Z", false),
        IconKind.Line => ("M5 19 L19 5", false),
        IconKind.Ellipse => ("M12 5 C17 5 21 8.1 21 12 C21 15.9 17 19 12 19 C7 19 3 15.9 3 12 C3 8.1 7 5 12 5 Z", false),
        IconKind.Move => ("M12 3 L12 21 M3 12 L21 12 M9 6 L12 3 L15 6 M9 18 L12 21 L15 18 M6 9 L3 12 L6 15 M18 9 L21 12 L18 15", false),
        _ => ("M4 4 L20 20", false),
    };
}
