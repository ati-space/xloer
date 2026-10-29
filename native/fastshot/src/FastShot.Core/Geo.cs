using System.Windows;

namespace FastShot.Core;

public static class Geo
{
    public static Rect RectBetween(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    public static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    public static double DistanceToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= 0) return Distance(p, a);
        var t = Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared));
        return Distance(p, new Point(a.X + t * dx, a.Y + t * dy));
    }

    /// <summary>Locks a direction to the nearest 45 degrees.</summary>
    public static Point SnapAngle(Point origin, Point p)
    {
        double dx = p.X - origin.X, dy = p.Y - origin.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        var step = Math.PI / 4;
        var angle = Math.Round(Math.Atan2(dy, dx) / step) * step;
        return new Point(origin.X + Math.Cos(angle) * length, origin.Y + Math.Sin(angle) * length);
    }

    /// <summary>Turns a drag into a square (or circle) anchored at <paramref name="origin"/>.</summary>
    public static Point SquareCorner(Point origin, Point p)
    {
        double dx = p.X - origin.X, dy = p.Y - origin.Y;
        var side = Math.Max(Math.Abs(dx), Math.Abs(dy));
        return new Point(origin.X + (dx < 0 ? -side : side), origin.Y + (dy < 0 ? -side : side));
    }

    /// <summary>Shrinks (positive) or grows (negative) a rectangle on every side. Never collapses below zero size.</summary>
    public static Rect Inset(this Rect r, double dx, double dy)
    {
        var w = Math.Max(0, r.Width - dx * 2);
        var h = Math.Max(0, r.Height - dy * 2);
        return new Rect(r.X + dx, r.Y + dy, w, h);
    }

    public static Rect Shifted(this Rect r, double dx, double dy) => new(r.X + dx, r.Y + dy, r.Width, r.Height);

    public static Point Center(this Rect r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    public static Rect Rounded(this Rect r) =>
        new(Math.Round(r.X), Math.Round(r.Y), Math.Max(1, Math.Round(r.Width)), Math.Max(1, Math.Round(r.Height)));

    /// <summary>Intersection that yields an empty-size rectangle instead of Rect.Empty.</summary>
    public static Rect Clip(this Rect r, Rect bounds)
    {
        var i = Rect.Intersect(r, bounds);
        return i.IsEmpty ? new Rect(0, 0, 0, 0) : i;
    }

    public static bool ContainsPoint(this Rect r, Point p) => p.X >= r.X && p.X <= r.Right && p.Y >= r.Y && p.Y <= r.Bottom;
}
