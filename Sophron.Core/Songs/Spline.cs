namespace Sophron.Songs;

/// <summary>
/// A smooth curve through a handful of points, for shaping velocities: a crescendo, a swell and fall.
/// It is monotone between neighbouring points (Fritsch and Carlson's cubic), so it never overshoots
/// them: a curve drawn from soft to loud only ever gets louder, and a curve drawn between 0 and 1
/// stays between them. Two points make a straight line.
/// </summary>
public sealed class Spline
{
    private readonly double[] _x;
    private readonly double[] _y;
    private readonly double[] _slope;

    /// <param name="points">The points, in any order; of two at the same x, the later is kept.</param>
    public Spline(IEnumerable<(double X, double Y)> points)
    {
        var sorted = points
            .Select((point, order) => (point, order))
            .GroupBy(entry => entry.point.X)
            .Select(group => group.MaxBy(entry => entry.order).point)
            .OrderBy(point => point.X)
            .ToArray();
        _x = [.. sorted.Select(point => point.X)];
        _y = [.. sorted.Select(point => point.Y)];
        _slope = Slopes(_x, _y);
    }

    public int Count => _x.Length;

    /// <summary>Where the curve starts; meaningless with fewer than two points.</summary>
    public double First => _x.Length == 0 ? 0 : _x[0];

    /// <summary>Where the curve ends.</summary>
    public double Last => _x.Length == 0 ? 0 : _x[^1];

    /// <summary>The curve's height at <paramref name="x"/>, or null outside it or if it has fewer than two points.</summary>
    public double? At(double x)
    {
        if (_x.Length < 2 || x < _x[0] || x > _x[^1])
        {
            return null;
        }

        var i = Array.BinarySearch(_x, x);
        if (i >= 0)
        {
            return _y[i];
        }

        i = ~i - 1;
        var h = _x[i + 1] - _x[i];
        var t = (x - _x[i]) / h;
        var t2 = t * t;
        var t3 = t2 * t;
        return (((2 * t3) - (3 * t2) + 1) * _y[i])
            + ((t3 - (2 * t2) + t) * h * _slope[i])
            + (((-2 * t3) + (3 * t2)) * _y[i + 1])
            + ((t3 - t2) * h * _slope[i + 1]);
    }

    /// <summary>The slope at each point, flattened wherever it would carry the curve past a neighbour.</summary>
    private static double[] Slopes(double[] x, double[] y)
    {
        var n = x.Length;
        var slopes = new double[n];
        if (n < 2)
        {
            return slopes;
        }

        var secants = new double[n - 1];
        for (var i = 0; i < n - 1; i++)
        {
            secants[i] = (y[i + 1] - y[i]) / (x[i + 1] - x[i]);
        }

        slopes[0] = secants[0];
        slopes[n - 1] = secants[n - 2];
        for (var i = 1; i < n - 1; i++)
        {
            // A peak or a trough, or a flat stretch on either side, is flat at the point itself.
            slopes[i] = secants[i - 1] * secants[i] <= 0 ? 0 : (secants[i - 1] + secants[i]) / 2;
        }

        for (var i = 0; i < n - 1; i++)
        {
            if (secants[i] == 0)
            {
                slopes[i] = slopes[i + 1] = 0;
                continue;
            }

            var a = slopes[i] / secants[i];
            var b = slopes[i + 1] / secants[i];
            var length = (a * a) + (b * b);
            if (length > 9)
            {
                var scale = 3 / Math.Sqrt(length);
                slopes[i] = scale * a * secants[i];
                slopes[i + 1] = scale * b * secants[i];
            }
        }

        return slopes;
    }
}
