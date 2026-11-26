namespace Wheel.Core.Screen;

/// <summary>
/// Builds one tall picture from pictures of the same screen area taken while it is scrolled (a scrolling
/// screenshot). Each new picture is matched against the previous one to find how far the content moved;
/// only the rows that came into view are added. A header or footer that stays put while the page scrolls
/// (a sticky menu bar, a cookie banner) is kept once: the header at the top, the footer at the very end.
/// Pictures are BGRA, 4 bytes per pixel, rows packed with no padding.
/// </summary>
public sealed class ScrollStitcher
{
    public enum Step
    {
        /// <summary>The first picture.</summary>
        First,
        /// <summary>The content moved down; new rows were added.</summary>
        Added,
        /// <summary>Nothing moved.</summary>
        Same,
        /// <summary>This picture couldn't be matched (scrolled up, too fast, or a different window): nothing added.</summary>
        Lost,
        /// <summary>The picture is as tall as allowed; nothing more is added.</summary>
        Full,
    }

    private readonly int _width, _height, _maxHeight, _rowBytes;
    private readonly List<byte[]> _rows = []; // the picture so far, without the footer
    private byte[]? _last;                      // the previous matched picture
    private ulong[] _lastSig = [];
    private int _lastTop;                       // where the previous picture's top row sits in the tall picture
    private int _footer;                        // rows at the bottom of the view that stay put

    public ScrollStitcher(int width, int height, int maxHeight = 20_000)
    {
        if (width < 8 || height < 16) throw new ArgumentException("The area is too small to scroll.");
        _width = width;
        _height = height;
        _maxHeight = Math.Max(height, maxHeight);
        _rowBytes = width * 4;
    }

    /// <summary>The height of the finished picture so far (the footer included).</summary>
    public int Height => _rows.Count + _footer;

    public Step Add(byte[] bgra)
    {
        if (bgra.Length < _rowBytes * _height) throw new ArgumentException("The picture is the wrong size.");
        var (sig, informative) = Signatures(bgra);

        if (_last is null)
        {
            for (var i = 0; i < _height; i++) _rows.Add(Row(bgra, i));
            Keep(bgra, sig, top: 0);
            return Step.First;
        }

        // Rows at the top and bottom that didn't change: a sticky header and footer (or plain background).
        int top = 0, bottom = 0;
        while (top < _height && sig[top] == _lastSig[top]) top++;
        if (top == _height) return Step.Same;
        while (bottom < _height - top && sig[_height - 1 - bottom] == _lastSig[_height - 1 - bottom]) bottom++;

        var dy = Offset(sig, informative, top, _height - bottom);
        if (dy == 0) return Step.Same;
        if (dy < 0) return Step.Lost;
        if (_rows.Count >= _maxHeight) return Step.Full;

        // Rows the previous picture added from its footer area are only kept if they weren't the footer.
        var newTop = _lastTop + dy;
        var extent = Math.Min(_rows.Count, _lastTop + _height - bottom);
        if (extent < _rows.Count) _rows.RemoveRange(extent, _rows.Count - extent);
        for (var i = Math.Max(0, extent - newTop); i < _height - bottom && _rows.Count < _maxHeight; i++)
            _rows.Add(Row(bgra, i));
        _footer = bottom;
        Keep(bgra, sig, newTop);
        return _rows.Count >= _maxHeight ? Step.Full : Step.Added;
    }

    /// <summary>The tall picture: everything added so far, then the footer from the last picture.</summary>
    public (byte[] Bgra, int Width, int Height) Compose()
    {
        if (_last is null) throw new InvalidOperationException("Nothing was captured.");
        var height = _rows.Count + _footer;
        var result = new byte[height * _rowBytes];
        for (var i = 0; i < _rows.Count; i++)
            Buffer.BlockCopy(_rows[i], 0, result, i * _rowBytes, _rowBytes);
        Buffer.BlockCopy(_last, (_height - _footer) * _rowBytes, result, _rows.Count * _rowBytes, _footer * _rowBytes);
        return (result, _width, height);
    }

    /// <summary>
    /// How far the content between rows <paramref name="from"/> and <paramref name="to"/> moved up since the
    /// previous picture: 0 = not at all, -1 = no match. Rows with something on them must agree; blank rows
    /// match anything, so they don't decide.
    /// </summary>
    private int Offset(ulong[] sig, bool[] informative, int from, int to)
    {
        var band = to - from;
        int best = -1, bestScore = 0;
        double bestRatio = 0;
        for (var dy = 0; dy < band - 4; dy++)
        {
            int score = 0, counted = 0;
            for (var i = from; i < to - dy; i++)
            {
                if (!informative[i]) continue;
                counted++;
                if (sig[i] == _lastSig[i + dy]) score++;
            }
            if (counted < 3) continue;
            var ratio = score / (double)counted;
            // Most rows with content must match; prefer more matching rows, then the smaller move.
            if (ratio >= 0.85 && (score > bestScore || (score == bestScore && ratio > bestRatio)))
            {
                best = dy;
                bestScore = score;
                bestRatio = ratio;
            }
        }
        return best;
    }

    /// <summary>
    /// A fingerprint of each row (every other pixel's brightness, slightly rounded so tiny colour noise doesn't
    /// count) and whether the row has anything on it. The right edge is left out: scroll bars move there.
    /// </summary>
    private (ulong[] Sig, bool[] Informative) Signatures(byte[] bgra)
    {
        var sig = new ulong[_height];
        var informative = new bool[_height];
        var right = _width - Math.Min(24, _width / 10);
        for (var y = 0; y < _height; y++)
        {
            ulong hash = 14695981039346656037UL;
            int min = 255, max = 0;
            var row = y * _rowBytes;
            for (var x = 0; x < right; x += 2)
            {
                var p = row + x * 4;
                var lum = (bgra[p + 2] * 77 + bgra[p + 1] * 150 + bgra[p] * 29) >> 10; // 0..63
                hash = (hash ^ (ulong)lum) * 1099511628211UL;
                if (lum < min) min = lum;
                if (lum > max) max = lum;
            }
            sig[y] = hash;
            informative[y] = max - min >= 4;
        }
        return (sig, informative);
    }

    private void Keep(byte[] bgra, ulong[] sig, int top)
    {
        _last = bgra;
        _lastSig = sig;
        _lastTop = top;
    }

    private byte[] Row(byte[] bgra, int y)
    {
        var row = new byte[_rowBytes];
        Buffer.BlockCopy(bgra, y * _rowBytes, row, 0, _rowBytes);
        return row;
    }
}
