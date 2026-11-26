using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using Wheel.App.Platform;
using Wheel.Core;
using Wheel.Core.Converters;

namespace Wheel.App;

/// <summary>
/// The media player: video and music in every format LibVLC knows. A playlist (the files chosen, or the folder),
/// seek, volume, speed, subtitles and audio tracks, equalizer, A-B repeat, frame snapshots, full screen, and it
/// remembers where you stopped in long files. Controls float over the picture and fade away while playing.
/// </summary>
internal sealed class PlayerWindow : Window
{
    private static int _volume = 100;
    private static readonly double[] Speeds = [0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 3];

    private readonly IPlatform _platform;
    private readonly string _snapshotFolder;
    private readonly LibVLC _vlc;
    private readonly MediaPlayer _player;
    private readonly FrameSink _frames;
    private readonly List<string> _playlist;
    private int _index;
    private Media? _media;

    private readonly Image _video = new() { Stretch = Stretch.Uniform };
    private readonly StackPanel _audioArt;
    private readonly TextBlock _audioTitle = new() { FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _audioDetail = new() { FontSize = 13, Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Slider _seek = new() { Minimum = 0, Maximum = 1, Focusable = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly Slider _volumeSlider = new() { Minimum = 0, Maximum = 125, Width = 96, Focusable = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _time = new() { Foreground = Brushes.White, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, MinWidth = 96 };
    private readonly TextBlock _title = new() { Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _osd = new() { Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeight.SemiBold };
    private readonly Border _osdBox;
    private readonly Border _top;
    private readonly Border _bottom;
    private readonly ListBox _list = new() { Background = Brushes.Transparent };
    private readonly Border _listPanel;
    private readonly Button _playButton;
    private readonly Button _muteButton;
    private readonly Button _repeatButton;
    private readonly StackPanel _empty;
    private readonly StackPanel _recent = new() { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromSeconds(2.5) };
    private readonly DispatcherTimer _osdTimer = new() { Interval = TimeSpan.FromSeconds(1.4) };
    private bool _settingSeek;
    private long? _loopA, _loopB;
    private uint _equalizer = uint.MaxValue; // none
    private WindowState _beforeFullScreen = WindowState.Normal;

    public PlayerWindow(IPlatform platform, LibVLC vlc, IReadOnlyList<string> playlist, int start, string snapshotFolder)
    {
        _platform = platform;
        _vlc = vlc;
        _snapshotFolder = snapshotFolder;
        _playlist = playlist.ToList();
        _index = _playlist.Count == 0 ? 0 : Math.Clamp(start, 0, _playlist.Count - 1);
        Ui.Window(this, "Desk Wheel Player");
        Width = 1040;
        Height = 640;
        MinWidth = 520;
        MinHeight = 320;
        Background = Brushes.Black;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        DragDrop.SetAllowDrop(this, true);

        _player = new MediaPlayer(vlc) { EnableHardwareDecoding = true };
        _frames = new FrameSink(this);
        _frames.Attach(_player);
        _player.Volume = _volume;

        // ---- picture, or the song's name for music
        _audioArt = new StackPanel
        {
            Spacing = 10, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(40),
            Children = { Center(Ui.Icon(IconKind.Music, 72, new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)))), _audioTitle, _audioDetail },
            IsVisible = false,
        };

        // ---- top: name, playlist and always-on-top
        var openFiles = Overlay(IconKind.Folder, "Open files… (" + (OperatingSystem.IsMacOS() ? "⌘O" : "Ctrl+O") + ")", () => _ = PickFilesAsync());
        var pin = Overlay(IconKind.Pin, "Keep on top", () => { Topmost = !Topmost; Osd(Topmost ? "Kept on top" : "Not on top"); });
        var listButton = Overlay(IconKind.List, "Playlist (L)", TogglePlaylist);
        _top = new Border
        {
            VerticalAlignment = VerticalAlignment.Top, Padding = new Thickness(16, 10, 10, 26),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(0xB0, 0, 0, 0), 0), new GradientStop(Colors.Transparent, 1) },
            },
            Child = new DockPanel { Children = { Docked(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { openFiles, pin, listButton } }, Dock.Right), _title } },
        };

        // ---- bottom: seek bar and controls
        _playButton = Overlay(IconKind.Pause, "Play / pause (Space)", TogglePlay, 22);
        _muteButton = Overlay(IconKind.Volume, "Mute (M)", ToggleMute);
        _repeatButton = Overlay(IconKind.Repeat, "A-B repeat (R): first press marks the start, second the end, third clears", ABRepeat);
        var speed = Overlay(IconKind.Speed, "Speed ([ and ])", () => { });
        speed.Flyout = SpeedMenu();
        var subtitles = Overlay(IconKind.Subtitles, "Subtitles (S)", () => { });
        subtitles.Flyout = SubtitlesMenu();
        var audio = Overlay(IconKind.Music, "Audio track and equalizer", () => { });
        audio.Flyout = AudioMenu();
        var snapshot = Overlay(IconKind.Camera, "Save this frame as a picture (P)", Snapshot);
        var full = Overlay(IconKind.Expand, "Full screen (F or double-click)", ToggleFullScreen);
        _volumeSlider.Value = _volume;
        _volumeSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty) return;
            _volume = (int)_volumeSlider.Value;
            _player.Volume = _volume;
            if (_player.Mute && _volume > 0) _player.Mute = false;
            UpdateMuteIcon();
        };
        _seek.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && !_settingSeek && _player.IsSeekable && _player.Length > 0)
                _player.Time = (long)(_seek.Value * _player.Length);
        };

        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Overlay(IconKind.Previous, "Previous (P / Page Up)", () => Go(-1)),
                Overlay(IconKind.Rewind, "Back 10 seconds (←)", () => Skip(-10)),
                _playButton,
                Overlay(IconKind.FastForward, "Forward 10 seconds (→)", () => Skip(10)),
                Overlay(IconKind.Next, "Next (N / Page Down)", () => Go(1)),
                _time,
            },
        };
        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center,
            Children = { _repeatButton, speed, subtitles, audio, snapshot, _muteButton, _volumeSlider, full },
        };
        var controls = new DockPanel { Children = { Docked(right, Dock.Right), left } };
        _bottom = new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom, Padding = new Thickness(14, 30, 10, 8),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Colors.Transparent, 0), new GradientStop(Color.FromArgb(0xC8, 0, 0, 0), 1) },
            },
            Child = new StackPanel { Spacing = 0, Children = { _seek, controls } },
        };

        // ---- right: the playlist
        _list.SelectionMode = SelectionMode.Single;
        _list.DoubleTapped += (_, _) => { if (_list.SelectedIndex >= 0) Open(_list.SelectedIndex); };
        _list.KeyDown += (_, e) => { if (e.Key == Key.Enter && _list.SelectedIndex >= 0) { Open(_list.SelectedIndex); e.Handled = true; } };
        _listPanel = new Border
        {
            Width = 300, HorizontalAlignment = HorizontalAlignment.Right, IsVisible = false,
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x16, 0x17, 0x1A)), Padding = new Thickness(6, 56, 6, 110),
            Child = _list,
        };

        // ---- nothing playing yet: drop, open or paste
        var paste = OperatingSystem.IsMacOS() ? "⌘V" : "Ctrl+V";
        var openButton = Ui.Primary("Open…", () => _ = PickFilesAsync(), IconKind.Folder);
        openButton.HorizontalAlignment = HorizontalAlignment.Center;
        _empty = new StackPanel
        {
            Spacing = 14, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, IsVisible = false,
            Children =
            {
                Center(WhiteIcon(IconKind.Play, 56)),
                new TextBlock { Text = "Drop videos or music here", FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = $"or paste copied files with {paste}", FontSize = 13, Foreground = new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Center },
                openButton,
                _recent,
            },
        };

        _osdBox = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 64, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 7),
            Child = _osd, IsVisible = false, IsHitTestVisible = false,
        };

        var stage = new Grid { Background = Brushes.Black, Children = { _video, _audioArt, _empty, _top, _bottom, _listPanel, _osdBox } };
        stage.DoubleTapped += (_, e) => { if (e.Source is Image or Grid or StackPanel) ToggleFullScreen(); };
        stage.PointerPressed += (_, e) =>
        {
            // A single click on the picture plays or pauses.
            if (e.Source is Image && e.GetCurrentPoint(stage).Properties.IsLeftButtonPressed && e.ClickCount == 1) TogglePlay();
        };
        Content = stage;

        // ---- behaviour
        PointerMoved += (_, _) => ShowControls();
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DropEvent, OnDrop);
        _tick.Tick += (_, _) => Tick();
        _hide.Tick += (_, _) => HideControls();
        _osdTimer.Tick += (_, _) => { _osdBox.IsVisible = false; _osdTimer.Stop(); };
        _player.EndReached += (_, _) => Dispatcher.UIThread.Post(() => { Forget(Current); Go(1, atEnd: true); });
        _player.EncounteredError += (_, _) => Dispatcher.UIThread.Post(() => Osd($"Can't play {Path.GetFileName(Current)}"));
        _player.Playing += (_, _) => Dispatcher.UIThread.Post(() => _playButton.Content = WhiteIcon(IconKind.Pause, 22));
        _player.Paused += (_, _) => Dispatcher.UIThread.Post(() => { _playButton.Content = WhiteIcon(IconKind.Play, 22); ShowControls(); });
        Closing += (_, _) => Remember();
        Closed += (_, _) => Shutdown();
        Opened += (_, _) =>
        {
            _tick.Start();
            FillPlaylist();
            if (_playlist.Count > 0) Open(_index); else ShowEmpty();
        };
    }

    private string Current => _playlist.Count > 0 ? _playlist[_index] : "";

    // ---------------------------------------------------------------- playing

    /// <summary>Opens playlist item <paramref name="index"/> (where it was left, for long files).</summary>
    private void Open(int index)
    {
        if (_playlist.Count == 0) { ShowEmpty(); return; }
        Remember();
        _empty.IsVisible = false;
        _bottom.IsVisible = true;
        _index = Math.Clamp(index, 0, _playlist.Count - 1);
        _loopA = _loopB = null;
        UpdateRepeat();
        _frames.Reset();
        var path = Current;
        var isAudio = FileClassifier.Classify(path) == FileKind.Audio;
        _video.Source = null;
        _audioArt.IsVisible = isAudio;
        _audioTitle.Text = Path.GetFileNameWithoutExtension(path);
        _audioDetail.Text = _playlist.Count > 1 ? $"{_index + 1} of {_playlist.Count}" : "";
        _title.Text = Path.GetFileName(path);
        Title = Path.GetFileName(path) + " · Desk Wheel Player";
        _list.SelectedIndex = _index;

        var old = _media;
        _media = new Media(_vlc, new Uri(path));
        _player.Play(_media);
        old?.Dispose();
        PlayerMemory.Played(path);
        if (PlayerMemory.Get(path) is { } resume)
        {
            _player.Time = resume;
            Osd($"Carrying on from {Clock(resume)} (Home to start over)");
        }
        ShowControls();
    }

    /// <summary>Nothing to play: the drop / open / paste screen, with what was played recently.</summary>
    private void ShowEmpty()
    {
        _empty.IsVisible = true;
        _audioArt.IsVisible = false;
        _video.Source = null;
        _title.Text = "Desk Wheel Player";
        _recent.Children.Clear();
        var recent = PlayerMemory.Recent().Where(File.Exists).Take(6).ToList();
        if (recent.Count > 0)
        {
            _recent.Children.Add(new TextBlock { Text = "RECENT", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(0, 14, 0, 4), HorizontalAlignment = HorizontalAlignment.Center });
            foreach (var path in recent)
            {
                var b = new Button
                {
                    Content = Path.GetFileName(path), Background = Brushes.Transparent, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
                    Padding = new Thickness(10, 4), MaxWidth = 460, Focusable = false,
                };
                ToolTip.SetTip(b, path);
                b.Click += (_, _) => Add([path]);
                _recent.Children.Add(b);
            }
        }
        ShowControls();
    }

    /// <summary>Adds files to the playlist and plays the first of them (one file: its folder comes too).</summary>
    public void Add(IReadOnlyList<string> paths)
    {
        var playable = paths.Where(p => File.Exists(p) && IsPlayable(p)).ToList();
        if (playable.Count == 0)
        {
            Osd("Those aren't videos or music");
            return;
        }
        if (_playlist.Count == 0)
        {
            var (list, start) = PlaylistFor(playable);
            _playlist.AddRange(list);
            FillPlaylist();
            Open(start);
            return;
        }
        var at = _playlist.Count;
        _playlist.AddRange(playable);
        FillPlaylist();
        Open(at);
    }

    private async Task PickFilesAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Play", AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Videos and music") { Patterns = MediaPatterns() },
                new FilePickerFileType("All files") { Patterns = ["*"] },
            ],
        });
        var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count > 0) Add(paths);
    }

    private static string[] MediaPatterns() =>
        ["*.mp4", "*.mkv", "*.mov", "*.avi", "*.webm", "*.wmv", "*.m4v", "*.flv", "*.3gp", "*.ts", "*.mts", "*.mpg", "*.mpeg",
         "*.mp3", "*.m4a", "*.aac", "*.wav", "*.flac", "*.ogg", "*.opus", "*.wma", "*.aiff", "*.aif"];

    private void Go(int step, bool atEnd = false)
    {
        var next = _index + step;
        if (next >= 0 && next < _playlist.Count)
        {
            Open(next);
            return;
        }
        if (atEnd)
        {
            _player.Stop();
            _playButton.Content = WhiteIcon(IconKind.Play, 22);
            ShowControls();
            Osd(_playlist.Count > 1 ? "End of the playlist" : "Finished");
        }
    }

    private void TogglePlay()
    {
        if (_playlist.Count == 0) return;
        if (_player.State is VLCState.Ended or VLCState.Stopped or VLCState.Error)
            Open(_index);
        else
        {
            _player.Pause();
        }
        ShowControls();
    }

    private void Skip(int seconds)
    {
        if (!_player.IsSeekable) return;
        var to = Math.Clamp(_player.Time + seconds * 1000L, 0, Math.Max(0, _player.Length - 500));
        _player.Time = to;
        Osd($"{(seconds > 0 ? "+" : "−")}{Math.Abs(seconds)} s · {Clock(to)}");
    }

    private void Tick()
    {
        var length = _player.Length;
        var time = Math.Max(0, _player.Time);
        _settingSeek = true;
        _seek.Value = length > 0 ? Math.Clamp((double)time / length, 0, 1) : 0;
        _settingSeek = false;
        _time.Text = length > 0 ? $"{Clock(time)} / {Clock(length)}" : Clock(time);
        if (_loopB is { } b && _loopA is { } a && time >= b) _player.Time = a;
        if (!_audioArt.IsVisible && _video.Source is null && _player.State == VLCState.Playing && _player.VideoTrackCount == 0)
        {
            _audioArt.IsVisible = true; // a video file with only sound
        }
    }

    private static string Clock(long ms)
    {
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    private void ToggleMute()
    {
        _player.Mute = !_player.Mute;
        UpdateMuteIcon();
        Osd(_player.Mute ? "Muted" : $"Volume {_player.Volume}%");
    }

    private void UpdateMuteIcon() => _muteButton.Content = WhiteIcon(_player.Mute || _volume == 0 ? IconKind.Mute : IconKind.Volume, 18);

    private void ChangeVolume(int by)
    {
        _volumeSlider.Value = Math.Clamp(_volume + by, 0, 125);
        Osd($"Volume {(int)_volumeSlider.Value}%");
    }

    private void SetSpeed(double speed)
    {
        _player.SetRate((float)speed);
        Osd($"Speed {speed:0.##}×");
    }

    private void StepSpeed(int direction)
    {
        var current = _player.Rate;
        var next = direction > 0 ? Speeds.FirstOrDefault(s => s > current + 0.01, Speeds[^1]) : Speeds.LastOrDefault(s => s < current - 0.01, Speeds[0]);
        SetSpeed(next);
    }

    private void ABRepeat()
    {
        if (_loopA is null) { _loopA = _player.Time; Osd($"Repeat from {Clock(_loopA.Value)}… press again at the end"); }
        else if (_loopB is null && _player.Time > _loopA + 500) { _loopB = _player.Time; _player.Time = _loopA.Value; Osd($"Repeating {Clock(_loopA.Value)} – {Clock(_loopB.Value)}"); }
        else { _loopA = _loopB = null; Osd("Repeat off"); }
        UpdateRepeat();
    }

    private void UpdateRepeat() =>
        _repeatButton.Foreground = _loopA is null ? Brushes.White : _loopB is null ? Ui.Info : Ui.Accent;

    private void Snapshot()
    {
        if (_video.Source is not WriteableBitmap frame)
        {
            Osd("No picture to save");
            return;
        }
        try
        {
            Directory.CreateDirectory(_snapshotFolder);
            var path = OutputPaths.Unique(Path.Combine(_snapshotFolder,
                $"{Path.GetFileNameWithoutExtension(Current)} at {Clock(_player.Time).Replace(':', '.')}.png"));
            frame.Save(path);
            AppHistory.Record("Screenshot", path, Current, "Frame from the player");
            Osd("Frame saved · " + Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Osd("Couldn't save the frame: " + ex.Message);
        }
    }

    // ---------------------------------------------------------------- menus

    private MenuFlyout SpeedMenu()
    {
        var menu = new MenuFlyout();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            foreach (var s in Speeds)
            {
                var item = new MenuItem { Header = s == 1 ? "Normal" : $"{s:0.##}×", Icon = Math.Abs(_player.Rate - s) < 0.01 ? Ui.Icon(IconKind.Check, 14) : null };
                item.Click += (_, _) => SetSpeed(s);
                menu.Items.Add(item);
            }
        };
        return menu;
    }

    private MenuFlyout SubtitlesMenu()
    {
        var menu = new MenuFlyout();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            foreach (var track in _player.SpuDescription)
            {
                var id = track.Id;
                var item = new MenuItem { Header = id < 0 ? "Off" : track.Name, Icon = _player.Spu == id ? Ui.Icon(IconKind.Check, 14) : null };
                item.Click += (_, _) => { _player.SetSpu(id); Osd(id < 0 ? "Subtitles off" : "Subtitles: " + track.Name); };
                menu.Items.Add(item);
            }
            if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "No subtitles in this file", IsEnabled = false });
            menu.Items.Add(new Separator());
            var load = new MenuItem { Header = "Add a subtitle file…" };
            load.Click += async (_, _) => await LoadSubtitlesAsync();
            menu.Items.Add(load);
            foreach (var (label, delta) in new[] { ("Subtitles earlier (−0.5 s)", -500_000L), ("Subtitles later (+0.5 s)", 500_000L) })
            {
                var shift = new MenuItem { Header = label };
                shift.Click += (_, _) => { _player.SetSpuDelay(_player.SpuDelay + delta); Osd($"Subtitle delay {_player.SpuDelay / 1_000_000.0:+0.0;−0.0;0} s"); };
                menu.Items.Add(shift);
            }
        };
        return menu;
    }

    private async Task LoadSubtitlesAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Subtitle file", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Subtitles") { Patterns = ["*.srt", "*.vtt", "*.ass", "*.ssa", "*.sub", "*.idx", "*.txt"] }],
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(Current)!),
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            _player.AddSlave(MediaSlaveType.Subtitle, new Uri(path).AbsoluteUri, true);
            Osd("Subtitles: " + Path.GetFileName(path));
        }
    }

    private MenuFlyout AudioMenu()
    {
        var menu = new MenuFlyout();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            var tracks = _player.AudioTrackDescription.Where(t => t.Id >= 0).ToList();
            if (tracks.Count > 1)
            {
                foreach (var track in tracks)
                {
                    var id = track.Id;
                    var item = new MenuItem { Header = track.Name, Icon = _player.AudioTrack == id ? Ui.Icon(IconKind.Check, 14) : null };
                    item.Click += (_, _) => { _player.SetAudioTrack(id); Osd("Audio: " + track.Name); };
                    menu.Items.Add(item);
                }
                menu.Items.Add(new Separator());
            }
            var eq = new MenuItem { Header = "Equalizer" };
            var off = new MenuItem { Header = "Off", Icon = _equalizer == uint.MaxValue ? Ui.Icon(IconKind.Check, 14) : null };
            off.Click += (_, _) => { _equalizer = uint.MaxValue; _player.UnsetEqualizer(); Osd("Equalizer off"); };
            eq.Items.Add(off);
            using (var names = new Equalizer())
            {
                for (uint i = 0; i < names.PresetCount; i++)
                {
                    var preset = i;
                    var name = names.PresetName(i);
                    var item = new MenuItem { Header = name, Icon = _equalizer == preset ? Ui.Icon(IconKind.Check, 14) : null };
                    item.Click += (_, _) =>
                    {
                        _equalizer = preset;
                        using var e = new Equalizer(preset);
                        _player.SetEqualizer(e);
                        Osd("Equalizer: " + name);
                    };
                    eq.Items.Add(item);
                }
            }
            menu.Items.Add(eq);
        };
        return menu;
    }

    // ---------------------------------------------------------------- playlist, full screen, controls

    private void FillPlaylist()
    {
        _list.ItemsSource = _playlist.Select((p, i) => $"{i + 1}.  {Path.GetFileName(p)}").ToList();
        _list.SelectedIndex = _index;
    }

    private void TogglePlaylist()
    {
        _listPanel.IsVisible = !_listPanel.IsVisible;
        if (_listPanel.IsVisible) _list.ScrollIntoView(_index);
        ShowControls();
    }

    private void ToggleFullScreen()
    {
        if (WindowState == WindowState.FullScreen)
            WindowState = _beforeFullScreen;
        else
        {
            _beforeFullScreen = WindowState;
            WindowState = WindowState.FullScreen;
        }
    }

    private void ShowControls()
    {
        _top.IsVisible = true;
        _bottom.IsVisible = _playlist.Count > 0;
        Cursor = Cursor.Default;
        _hide.Stop();
        _hide.Start();
    }

    /// <summary>Only while a video is playing and nothing (a menu, the playlist) is open over it.</summary>
    private void HideControls()
    {
        _hide.Stop();
        if (!_player.IsPlaying || _audioArt.IsVisible || _listPanel.IsVisible || _bottom.IsPointerOver || _top.IsPointerOver) return;
        _top.IsVisible = _bottom.IsVisible = false;
        Cursor = new Cursor(StandardCursorType.None);
    }

    private void Osd(string text)
    {
        _osd.Text = text;
        _osdBox.IsVisible = true;
        _osdTimer.Stop();
        _osdTimer.Start();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (_listPanel.IsVisible && _list.IsKeyboardFocusWithin && e.Key is Key.Up or Key.Down or Key.Enter) return;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (command && e.Key == Key.V)
        {
            if (_platform.ReadClipboard()?.Files is { Length: > 0 } copied) Add(copied);
            else Osd("Copy a video or song in " + (OperatingSystem.IsMacOS() ? "Finder" : "File Explorer") + " first");
            e.Handled = true;
            return;
        }
        if (command && e.Key == Key.O)
        {
            _ = PickFilesAsync();
            e.Handled = true;
            return;
        }
        if (_playlist.Count == 0 && e.Key != Key.Escape) return;
        switch (e.Key)
        {
            case Key.Space or Key.K or Key.MediaPlayPause: TogglePlay(); break;
            case Key.Left: Skip(shift ? -60 : -10); break;
            case Key.Right: Skip(shift ? 60 : 10); break;
            case Key.J: Skip(-10); break;
            case Key.Up: ChangeVolume(5); break;
            case Key.Down: ChangeVolume(-5); break;
            case Key.M: ToggleMute(); break;
            case Key.F or Key.F11: ToggleFullScreen(); break;
            case Key.Enter when e.KeyModifiers.HasFlag(KeyModifiers.Alt): ToggleFullScreen(); break;
            case Key.Escape when WindowState == WindowState.FullScreen: ToggleFullScreen(); break;
            case Key.Escape: Close(); break;
            case Key.N or Key.PageDown or Key.MediaNextTrack: Go(1); break;
            case Key.B or Key.PageUp or Key.MediaPreviousTrack: Go(-1); break;
            case Key.OemOpenBrackets: StepSpeed(-1); break;
            case Key.OemCloseBrackets: StepSpeed(1); break;
            case Key.OemBackslash or Key.D0: SetSpeed(1); break;
            case Key.R: ABRepeat(); break;
            case Key.P: Snapshot(); break;
            case Key.L: TogglePlaylist(); break;
            case Key.S: _player.SetSpu(NextSubtitle()); break;
            case Key.Home: _player.Time = 0; Osd("From the start"); break;
            case Key.OemPeriod when !_player.IsPlaying: _player.NextFrame(); break;
            default: return;
        }
        e.Handled = true;
        ShowControls();
    }

    private int NextSubtitle()
    {
        var tracks = _player.SpuDescription.Select(t => t.Id).ToList();
        if (tracks.Count == 0) { Osd("No subtitles in this file"); return -1; }
        var next = tracks[(tracks.IndexOf(_player.Spu) + 1) % tracks.Count];
        var name = _player.SpuDescription.FirstOrDefault(t => t.Id == next).Name;
        Osd(next < 0 ? "Subtitles off" : "Subtitles: " + name);
        return next;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var dropped = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (dropped is { Count: > 0 }) Add(dropped);
    }

    // ---------------------------------------------------------------- where you left off

    private void Remember()
    {
        if (_index < 0 || _index >= _playlist.Count) return;
        var length = _player.Length;
        var time = _player.Time;
        // Long files only, and not right at the start or end.
        if (length > 10 * 60_000 && time > 60_000 && time < length - 60_000) PlayerMemory.Set(Current, time);
        else if (time > 0) PlayerMemory.Forget(Current);
    }

    private static void Forget(string path) => PlayerMemory.Forget(path);

    private void Shutdown()
    {
        _tick.Stop();
        _hide.Stop();
        var player = _player;
        var media = _media;
        // Stopping waits for LibVLC's threads: never on the UI thread.
        Task.Run(() =>
        {
            try
            {
                player.Stop();
                player.Dispose();
                media?.Dispose();
            }
            catch (Exception ex) when (ex is VLCException or ObjectDisposedException) { Log.Write(ex); }
            finally { _frames.Dispose(); }
        });
    }

    // ---------------------------------------------------------------- helpers

    public static bool IsPlayable(string path) => FileClassifier.Classify(path) is FileKind.Video or FileKind.Audio;

    /// <summary>What to play: the chosen files, or (for one file) its whole folder, starting with it.</summary>
    public static (List<string> Playlist, int Start) PlaylistFor(IReadOnlyList<string> paths)
    {
        var files = paths.Where(p => File.Exists(p) && IsPlayable(p)).ToList();
        if (files.Count != 1) return (files, 0);
        var folder = Path.GetDirectoryName(files[0])!;
        var kind = FileClassifier.Classify(files[0]);
        var all = Directory.EnumerateFiles(folder).Where(p => FileClassifier.Classify(p) == kind)
            .OrderBy(Path.GetFileName, Wheel.Core.Files.NaturalComparer.Instance).ToList();
        var start = all.FindIndex(p => string.Equals(p, files[0], StringComparison.OrdinalIgnoreCase));
        return start < 0 ? (files, 0) : (all, start);
    }

    private static Control Center(Control c)
    {
        c.HorizontalAlignment = HorizontalAlignment.Center;
        return c;
    }

    private static Control Docked(Control c, Dock dock)
    {
        DockPanel.SetDock(c, dock);
        return c;
    }

    private static Control WhiteIcon(IconKind kind, double size) => Ui.Icon(kind, size, Brushes.White);

    private static Button Overlay(IconKind icon, string tip, Action click, double size = 18)
    {
        var b = new Button
        {
            Content = WhiteIcon(icon, size), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Padding = new Thickness(8), Focusable = false, Foreground = Brushes.White, Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    // ---------------------------------------------------------------- frames

    /// <summary>
    /// LibVLC draws each frame into one of three buffers; the window copies the newest finished one to the screen.
    /// Big videos are drawn at most 2560 × 1600 (plenty for any window, and a fraction of the memory of 4K).
    /// </summary>
    private sealed class FrameSink : IDisposable
    {
        private const uint MaxWidth = 2560, MaxHeight = 1600;
        private readonly PlayerWindow _window;
        private readonly object _sync = new();
        private readonly byte[][] _buffers = new byte[3][];
        private int _width, _height, _writing = -1, _ready = -1, _reading = -1, _generation;
        private bool _posted, _disposed;
        // Kept here so the garbage collector never frees a callback LibVLC still holds.
        private readonly MediaPlayer.LibVLCVideoFormatCb _format;
        private readonly MediaPlayer.LibVLCVideoCleanupCb _cleanup;
        private readonly MediaPlayer.LibVLCVideoLockCb _lock;
        private readonly MediaPlayer.LibVLCVideoDisplayCb _display;

        public FrameSink(PlayerWindow window)
        {
            _window = window;
            _format = Format;
            _cleanup = Cleanup;
            _lock = Lock;
            _display = Display;
        }

        public void Attach(MediaPlayer player)
        {
            player.SetVideoFormatCallbacks(_format, _cleanup);
            player.SetVideoCallbacks(_lock, null, _display);
        }

        public void Reset()
        {
            lock (_sync) { _ready = -1; _generation++; }
        }

        private uint Format(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
        {
            // "RV32": 32-bit BGRX, the same layout as Avalonia's BGRA (the 4th byte is ignored).
            Marshal.Copy("RV32"u8.ToArray(), 0, chroma, 4);
            var scale = Math.Min(1.0, Math.Min((double)MaxWidth / Math.Max(1, width), (double)MaxHeight / Math.Max(1, height)));
            width = (uint)Math.Max(2, Math.Round(width * scale / 2) * 2);
            height = (uint)Math.Max(2, Math.Round(height * scale / 2) * 2);
            pitches = width * 4;
            lines = height;
            lock (_sync)
            {
                FreeBuffers();
                _width = (int)width;
                _height = (int)height;
                for (var i = 0; i < 3; i++)
                {
                    _buffers[i] = GC.AllocateUninitializedArray<byte>((int)(pitches * lines), pinned: true);
                }
                _writing = _ready = _reading = -1;
                _generation++;
            }
            return 1;
        }

        private void Cleanup(ref IntPtr opaque)
        {
            lock (_sync) { FreeBuffers(); _ready = -1; }
        }

        private IntPtr Lock(IntPtr opaque, IntPtr planes)
        {
            lock (_sync)
            {
                var i = 0;
                while (i == _ready || i == _reading) i++;
                _writing = i;
                Marshal.WriteIntPtr(planes, Marshal.UnsafeAddrOfPinnedArrayElement(_buffers[i], 0));
            }
            return IntPtr.Zero;
        }

        private void Display(IntPtr opaque, IntPtr picture)
        {
            lock (_sync)
            {
                _ready = _writing;
                if (_posted || _disposed) return;
                _posted = true;
            }
            Dispatcher.UIThread.Post(Present, DispatcherPriority.Render);
        }

        /// <summary>On the UI thread: the newest frame onto the screen.</summary>
        private void Present()
        {
            int index, width, height, generation;
            lock (_sync)
            {
                _posted = false;
                if (_ready < 0 || _disposed) return;
                index = _reading = _ready;
                _ready = -1;
                (width, height, generation) = (_width, _height, _generation);
            }
            try
            {
                var bitmap = _window._video.Source as WriteableBitmap;
                if (bitmap is null || bitmap.PixelSize.Width != width || bitmap.PixelSize.Height != height)
                {
                    bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                    var old = _window._video.Source as IDisposable;
                    _window._video.Source = bitmap;
                    old?.Dispose();
                    _window._audioArt.IsVisible = false;
                }
                using (var fb = bitmap.Lock())
                {
                    var source = _buffers[index];
                    var row = width * 4;
                    if (fb.RowBytes == row)
                        Marshal.Copy(source, 0, fb.Address, row * height);
                    else
                        for (var y = 0; y < height; y++) Marshal.Copy(source, y * row, fb.Address + y * fb.RowBytes, row);
                }
                _window._video.InvalidateVisual();
            }
            finally
            {
                lock (_sync) _reading = -1;
            }
        }

        private void FreeBuffers()
        {
            for (var i = 0; i < 3; i++) _buffers[i] = [];
        }

        public void Dispose()
        {
            lock (_sync) { _disposed = true; FreeBuffers(); }
        }
    }
}

/// <summary>Where you stopped in long videos (and audiobooks), so they carry on from there.</summary>
internal static class PlayerMemory
{
    private const int Keep = 200;
    private static readonly string File_ = Path.Combine(Path.GetDirectoryName(MediaEngine.Packs.Root)!, "player-positions.json");
    private static Dictionary<string, long>? _positions;

    private static Dictionary<string, long> Positions
    {
        get
        {
            if (_positions is not null) return _positions;
            try
            {
                _positions = File.Exists(File_) ? JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(File_)) ?? [] : [];
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _positions = [];
            }
            return _positions;
        }
    }

    public static long? Get(string path) => Positions.TryGetValue(path, out var t) ? t : null;

    private static readonly string RecentFile = Path.Combine(Path.GetDirectoryName(MediaEngine.Packs.Root)!, "player-recent.json");
    private static List<string>? _recent;

    /// <summary>Recently played files, newest first.</summary>
    public static IReadOnlyList<string> Recent()
    {
        if (_recent is not null) return _recent;
        try { _recent = File.Exists(RecentFile) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(RecentFile)) ?? [] : []; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { _recent = []; }
        return _recent;
    }

    public static void Played(string path)
    {
        var list = Recent().Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).Prepend(path).Take(20).ToList();
        _recent = list;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RecentFile)!);
            File.WriteAllText(RecentFile, JsonSerializer.Serialize(list));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Write(ex); }
    }

    public static void Set(string path, long ms)
    {
        Positions.Remove(path);
        Positions[path] = ms; // newest last
        while (Positions.Count > Keep) Positions.Remove(Positions.Keys.First());
        Save();
    }

    public static void Forget(string path)
    {
        if (Positions.Remove(path)) Save();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
            File.WriteAllText(File_, JsonSerializer.Serialize(Positions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Write(ex); }
    }
}
