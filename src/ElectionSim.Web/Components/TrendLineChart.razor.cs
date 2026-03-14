using System.Text;
using ElectionSim.Core.Models;
using ElectionSim.Web.Services;
using Microsoft.AspNetCore.Components;

namespace ElectionSim.Web.Components;

/// <summary>
/// Code-behind for the SVG trend line chart. All geometry is computed in an abstract
/// coordinate space (SVG viewBox 700x155). The chart data area occupies x=[70,610],
/// y=[5,120] within it, leaving room for axis labels and end-of-line value labels.
/// </summary>
public partial class TrendLineChart
{
    [Parameter] public List<SimulationTrendPoint>? Points { get; set; }
    [Parameter] public Func<SimulationTrendPoint, Party, double> ValueSelector { get; set; } = (_, _) => 0;
    [Parameter] public Func<SimulationTrendPoint, Party, double>? SecondaryValueSelector { get; set; }
    [Parameter] public string? PrimaryLabel { get; set; }
    [Parameter] public string? SecondaryLabel { get; set; }
    [Parameter] public string YAxisLabel { get; set; } = "";
    [Parameter] public double? YMin { get; set; }
    [Parameter] public double? YMax { get; set; }
    [Parameter] public Func<double, string>? FormatFunc { get; set; }
    [Parameter] public EventCallback<SimulationTrendPoint> OnPointClicked { get; set; }
    [Parameter] public Func<SimulationTrendPoint, Party, double>? OuterBandUpperSelector { get; set; }
    [Parameter] public Func<SimulationTrendPoint, Party, double>? OuterBandLowerSelector { get; set; }
    [Parameter] public Func<SimulationTrendPoint, Party, double>? InnerBandUpperSelector { get; set; }
    [Parameter] public Func<SimulationTrendPoint, Party, double>? InnerBandLowerSelector { get; set; }

    private int hoveredIndex = -1;

    internal string FormatValue(double val) => FormatFunc != null ? FormatFunc(val) : val.ToString("F1");

    internal List<Party> GetVisibleParties()
    {
        if (Points == null || Points.Count == 0) return new();

        return PartyColorProvider.MainParties
            .Where(p => Points.Any(pt => ValueSelector(pt, p) > 0.5))
            .ToList();
    }

    internal double[] ComputeXPositions()
    {
        if (Points == null || Points.Count == 0) return Array.Empty<double>();

        const double left = 70;   // room for Y-axis tick labels
        const double right = 610; // room for end-of-line value labels (viewBox width = 700)

        if (Points.Count == 1) return new[] { (left + right) / 2 };

        var positions = new double[Points.Count];
        for (int i = 0; i < Points.Count; i++)
        {
            positions[i] = left + (double)i / (Points.Count - 1) * (right - left);
        }
        return positions;
    }

    internal record YScaleInfo(double Min, double Max, List<double> Ticks)
    {
        /// <summary>
        /// Maps a data value to SVG Y coordinate. SVG Y-axis is inverted (0 = top),
        /// so higher data values map to lower Y pixels. The chart data area spans
        /// Y pixels 5 (top) to 120 (bottom). Fallback value 62 is the vertical midpoint.
        /// </summary>
        public double ToY(double value)
        {
            double range = Max - Min;
            if (range <= 0) return 62;
            return 120 - (value - Min) / range * 115 + 5;
        }
    }

    internal YScaleInfo ComputeYScale()
    {
        if (Points == null || Points.Count == 0)
            return new YScaleInfo(0, 100, new List<double> { 0, 50, 100 });

        var parties = GetVisibleParties();
        double dataMin = double.MaxValue;
        double dataMax = double.MinValue;

        foreach (var pt in Points)
        {
            foreach (var p in parties)
            {
                double v = ValueSelector(pt, p);
                if (v < dataMin) dataMin = v;
                if (v > dataMax) dataMax = v;

                if (SecondaryValueSelector != null)
                {
                    double sv = SecondaryValueSelector(pt, p);
                    if (sv < dataMin) dataMin = sv;
                    if (sv > dataMax) dataMax = sv;
                }

                if (OuterBandUpperSelector != null)
                {
                    double upper = OuterBandUpperSelector(pt, p);
                    if (upper > dataMax) dataMax = upper;
                }
                if (OuterBandLowerSelector != null)
                {
                    double lower = OuterBandLowerSelector(pt, p);
                    if (lower < dataMin) dataMin = lower;
                }
            }
        }

        // Pad data bounds by 10% on each side for visual breathing room.
        // The max-min < 1 guard prevents a degenerate zero-range axis.
        double min = YMin ?? Math.Floor(dataMin * 0.9);
        double max = YMax ?? Math.Ceiling(dataMax * 1.1);
        if (max - min < 1) { min -= 1; max += 1; }

        var ticks = new List<double>();
        double step = NiceStep(max - min, 5);
        double tickStart = Math.Ceiling(min / step) * step;

        // Snap max up to the next tick boundary so confidence bands
        // don't extend above the last gridline and appear cut off.
        if (YMax == null)
            max = Math.Ceiling(max / step) * step;

        for (double t = tickStart; t <= max; t += step)
            ticks.Add(t);

        return new YScaleInfo(min, max, ticks);
    }

    /// <summary>
    /// Finds a "nice" human-readable tick step for the Y axis (Heckbert's algorithm).
    /// Given the data range and a target number of ticks, finds the order of magnitude
    /// then snaps to the nearest of {1, 2, 5, 10} multiples. The thresholds 1.5, 3, 7
    /// are the midpoints between those nice numbers.
    /// </summary>
    private static double NiceStep(double range, int targetTicks)
    {
        double rough = range / targetTicks;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        double normalized = rough / mag;

        if (normalized < 1.5) return 1 * mag;
        if (normalized < 3) return 2 * mag;
        if (normalized < 7) return 5 * mag;
        return 10 * mag;
    }

    /// <summary>
    /// Builds an SVG polygon "points" string for a confidence band. The polygon traces the
    /// upper bound left-to-right, then the lower bound right-to-left, forming a closed shape.
    /// </summary>
    internal string BuildBandPolygonPoints(Func<SimulationTrendPoint, Party, double> upperSelector, Func<SimulationTrendPoint, Party, double> lowerSelector, Party party, double[] xPositions, YScaleInfo yScale)
    {
        if (Points == null) return "";

        var sb = new StringBuilder();
        // Forward pass: upper bound left-to-right
        for (int i = 0; i < Points.Count; i++)
        {
            double x = xPositions[i];
            double y = yScale.ToY(upperSelector(Points[i], party));
            if (i > 0) sb.Append(' ');
            sb.Append(FormattableString.Invariant($"{x:F1},{y:F1}"));
        }
        // Reverse pass: lower bound right-to-left
        for (int i = Points.Count - 1; i >= 0; i--)
        {
            double x = xPositions[i];
            double y = yScale.ToY(lowerSelector(Points[i], party));
            sb.Append(' ');
            sb.Append(FormattableString.Invariant($"{x:F1},{y:F1}"));
        }
        return sb.ToString();
    }

    internal string BuildPolylinePoints(Party party, double[] xPositions, YScaleInfo yScale)
    {
        return BuildPolylinePointsFor(ValueSelector, party, xPositions, yScale);
    }

    internal string BuildSecondaryPolylinePoints(Party party, double[] xPositions, YScaleInfo yScale)
    {
        return SecondaryValueSelector != null
            ? BuildPolylinePointsFor(SecondaryValueSelector, party, xPositions, yScale)
            : "";
    }

    private string BuildPolylinePointsFor(Func<SimulationTrendPoint, Party, double> selector, Party party, double[] xPositions, YScaleInfo yScale)
    {
        if (Points == null) return "";

        var sb = new StringBuilder();
        for (int i = 0; i < Points.Count; i++)
        {
            double val = selector(Points[i], party);
            double x = xPositions[i];
            double y = yScale.ToY(val);
            if (i > 0) sb.Append(' ');
            sb.Append(FormattableString.Invariant($"{x:F1},{y:F1}"));
        }
        return sb.ToString();
    }

    internal record EndLabel(double NaturalY, double AdjustedY, string Label, string Color, bool IsPrimary);

    internal List<EndLabel> ComputeEndLabels(YScaleInfo yScale)
    {
        if (Points == null || Points.Count == 0) return new();

        var lastPt = Points[Points.Count - 1];
        var parties = GetVisibleParties();
        var labels = new List<EndLabel>();

        foreach (var party in parties)
        {
            var color = PartyColorProvider.GetColor(party);
            double val = ValueSelector(lastPt, party);
            double y = yScale.ToY(val);
            labels.Add(new EndLabel(y, y, FormatValue(val), color, true));
        }

        if (SecondaryValueSelector != null)
        {
            foreach (var party in parties)
            {
                var color = PartyColorProvider.GetColor(party);
                double val = SecondaryValueSelector(lastPt, party);
                double y = yScale.ToY(val);
                labels.Add(new EndLabel(y, y, FormatValue(val), color, false));
            }
        }

        // Resolve overlapping end-of-line labels. Labels at the right edge can overlap
        // when parties have similar values. This algorithm identifies clusters of labels
        // closer than minGap pixels apart, then spreads each cluster evenly around its
        // natural midpoint. Labels are clamped to stay within the chart area [5, 120].
        labels.Sort((a, b) => a.NaturalY.CompareTo(b.NaturalY));
        const double minGap = 11;
        int i = 0;
        while (i < labels.Count)
        {
            // Find the extent of this overlapping cluster
            int clusterStart = i;
            while (i + 1 < labels.Count && labels[i + 1].NaturalY - labels[i].NaturalY < minGap)
                i++;
            int clusterEnd = i;
            i++;

            if (clusterStart == clusterEnd) continue; // single label, no overlap

            int count = clusterEnd - clusterStart + 1;
            double naturalMid = (labels[clusterStart].NaturalY + labels[clusterEnd].NaturalY) / 2;
            double totalSpan = (count - 1) * minGap;
            double top = naturalMid - totalSpan / 2;

            // Clamp within chart area (5 to 120)
            if (top < 5) top = 5;
            if (top + totalSpan > 120) top = 120 - totalSpan;

            for (int j = 0; j < count; j++)
            {
                int idx = clusterStart + j;
                labels[idx] = labels[idx] with { AdjustedY = top + j * minGap };
            }
        }

        return labels;
    }

    internal List<int> GetXLabelIndices()
    {
        if (Points == null || Points.Count == 0) return new();

        int maxLabels = 8;
        if (Points.Count <= maxLabels)
            return Enumerable.Range(0, Points.Count).ToList();

        var indices = new List<int> { 0 };
        double step = (double)(Points.Count - 1) / (maxLabels - 1);
        for (int i = 1; i < maxLabels - 1; i++)
            indices.Add((int)Math.Round(i * step));
        indices.Add(Points.Count - 1);
        return indices.Distinct().ToList();
    }
}
