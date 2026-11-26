namespace Wheel.Core.Disk;

/// <summary>
/// One wedge of the disk chart. Angles are fractions of a full turn, clockwise from 12 o'clock.
/// Ring 1 is the folder's contents, ring 2 what's inside those, and so on.
/// </summary>
public sealed record SunburstArc(
    DiskNode? Node, int Ring, double Start, double Sweep, double Hue,
    long Size, int GroupCount = 0, bool IsFreeSpace = false)
{
    /// <summary>Several small items shown as one wedge ("312 small items").</summary>
    public bool IsGroup => GroupCount > 0;
    public double End => Start + Sweep;
}

public static class SunburstLayout
{
    /// <summary>
    /// The wedges for a folder: up to <paramref name="rings"/> rings deep. Items smaller than
    /// <paramref name="minSweep"/> of a turn are joined into one "small items" wedge per folder, and nothing is
    /// drawn inside them. <paramref name="freeSpace"/> (when the folder is a whole drive) takes its share of the
    /// first ring, in grey.
    /// </summary>
    public static List<SunburstArc> Layout(DiskNode centre, int rings = 6, double minSweep = 0.0025, long freeSpace = 0)
    {
        var arcs = new List<SunburstArc>();
        var total = (double)centre.Size + Math.Max(0, freeSpace);
        if (total <= 0) return arcs;

        // Colour by where each top-level item sits around the circle, so neighbours have related colours.
        void Place(DiskNode folder, int ring, double start, double sweep, double? hue)
        {
            if (ring > rings || folder.Size <= 0) return;
            var position = start;
            var small = new List<DiskNode>();
            foreach (var child in folder.Children)
            {
                if (child.Size <= 0) continue;
                var childSweep = sweep * child.Size / folder.Size;
                if (childSweep < minSweep)
                {
                    small.Add(child);
                    continue;
                }
                var childHue = hue ?? (position + childSweep / 2) / Math.Max(1e-9, (double)centre.Size / total) * 360 % 360;
                arcs.Add(new SunburstArc(child, ring, position, childSweep, childHue, child.Size));
                if (child.IsFolder) Place(child, ring + 1, position, childSweep, childHue);
                position += childSweep;
            }
            if (small.Count > 0)
            {
                var size = small.Sum(s => s.Size);
                var groupSweep = sweep * size / folder.Size;
                if (groupSweep >= minSweep / 4)
                    arcs.Add(new SunburstArc(small.Count == 1 ? small[0] : null, ring, position, groupSweep,
                        hue ?? 0, size, GroupCount: small.Count == 1 ? 0 : small.Count));
            }
        }

        var used = centre.Size / total;
        Place(centre, 1, 0, used, null);
        if (freeSpace > 0)
            arcs.Add(new SunburstArc(null, 1, used, 1 - used, 0, freeSpace, IsFreeSpace: true));
        return arcs;
    }

    /// <summary>The wedge under a point given as an angle (fraction of a turn from 12 o'clock) and a ring.</summary>
    public static SunburstArc? HitTest(IReadOnlyList<SunburstArc> arcs, int ring, double angle)
    {
        angle = ((angle % 1) + 1) % 1;
        foreach (var a in arcs)
            if (a.Ring == ring && angle >= a.Start && angle < a.End) return a;
        return null;
    }
}
