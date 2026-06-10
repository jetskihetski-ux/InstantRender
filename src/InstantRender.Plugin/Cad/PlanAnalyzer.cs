using InstantRender.Model;

namespace InstantRender.Cad;

/// <summary>
/// Turns raw, bucketed geometry into a structured <see cref="PlanModel"/>:
/// builds wall centerlines, derives rooms from closed loops, attaches the
/// nearest text label as a room name, and snaps door/window markers to walls.
///
/// This is intentionally pure (no AutoCAD types) so it is unit-testable
/// without AutoCAD. The MVP keeps detection simple per the spec.
/// </summary>
public sealed class PlanAnalyzer
{
    public AnalyzerOptions Options { get; }

    public PlanAnalyzer(AnalyzerOptions? options = null)
        => Options = options ?? new AnalyzerOptions();

    public PlanModel Build(RawGeometry raw, string? sourceFile = null)
    {
        var model = new PlanModel
        {
            SourceFile = sourceFile,
            WallHeight = Options.WallHeight
        };

        BuildWalls(raw, model);
        BuildRooms(raw, model);
        BuildOpenings(raw, model);
        ComputeBounds(model);

        return model;
    }

    private void BuildWalls(RawGeometry raw, PlanModel model)
    {
        int id = 1;
        foreach (var seg in raw.WallSegments)
        {
            // Skip zero-length / dust segments.
            if (Distance(seg.Start, seg.End) < Options.MinWallLength)
                continue;

            model.Walls.Add(new Wall
            {
                Id = id++,
                Start = seg.Start,
                End = seg.End,
                Thickness = Options.WallThickness
            });
        }
    }

    private void BuildRooms(RawGeometry raw, PlanModel model)
    {
        int id = 1;
        foreach (var loop in raw.ClosedLoops)
        {
            if (loop.Count < 3) continue;
            var area = PolygonArea(loop);
            if (area < Options.MinRoomArea) continue;

            var center = Centroid(loop);
            var room = new Room
            {
                Id = id++,
                Outline = loop,
                Area = area,
                Name = NearestLabel(raw.Labels, center)
            };
            model.Rooms.Add(room);
        }
    }

    private void BuildOpenings(RawGeometry raw, PlanModel model)
    {
        int id = 1;
        foreach (var m in raw.DoorMarkers)
        {
            model.Doors.Add(new Opening
            {
                Id = id++,
                Kind = OpeningKind.Door,
                Center = m.Center,
                Width = m.Width > 0 ? m.Width : Options.DefaultDoorWidth,
                SillHeight = 0.0,
                Height = Options.DoorHeight,
                HostWallId = NearestWallId(model.Walls, m.Center)
            });
        }

        foreach (var m in raw.WindowMarkers)
        {
            model.Windows.Add(new Opening
            {
                Id = id++,
                Kind = OpeningKind.Window,
                Center = m.Center,
                Width = m.Width > 0 ? m.Width : Options.DefaultWindowWidth,
                SillHeight = Options.WindowSillHeight,
                Height = Options.WindowHeight,
                HostWallId = NearestWallId(model.Walls, m.Center)
            });
        }
    }

    private static void ComputeBounds(PlanModel model)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        void Acc(Point2 p)
        {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }

        foreach (var w in model.Walls) { Acc(w.Start); Acc(w.End); }
        foreach (var r in model.Rooms) foreach (var p in r.Outline) Acc(p);

        if (minX == double.MaxValue) return; // nothing to bound
        model.Bounds = new BoundingBox
        {
            Min = new Point2(minX, minY),
            Max = new Point2(maxX, maxY)
        };
    }

    // ---- geometry helpers -------------------------------------------------

    private static double Distance(Point2 a, Point2 b)
    {
        var dx = a.X - b.X; var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static string? NearestLabel(List<Label> labels, Point2 to)
    {
        string? best = null;
        double bestDist = double.MaxValue;
        foreach (var l in labels)
        {
            var d = Distance(l.Position, to);
            if (d < bestDist) { bestDist = d; best = l.Text; }
        }
        return string.IsNullOrWhiteSpace(best) ? null : best.Trim();
    }

    private static int? NearestWallId(List<Wall> walls, Point2 point)
    {
        int? best = null;
        double bestDist = double.MaxValue;
        foreach (var w in walls)
        {
            var d = PointToSegment(point, w.Start, w.End);
            if (d < bestDist) { bestDist = d; best = w.Id; }
        }
        return best;
    }

    private static double PointToSegment(Point2 p, Point2 a, Point2 b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-12) return Distance(p, a);
        double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
        t = Math.Clamp(t, 0, 1);
        var proj = new Point2(a.X + t * dx, a.Y + t * dy);
        return Distance(p, proj);
    }

    /// <summary>Signed polygon area via the shoelace formula (abs value).</summary>
    private static double PolygonArea(List<Point2> poly)
    {
        double sum = 0;
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return Math.Abs(sum) * 0.5;
    }

    private static Point2 Centroid(List<Point2> poly)
    {
        double x = 0, y = 0;
        foreach (var p in poly) { x += p.X; y += p.Y; }
        return new Point2(x / poly.Count, y / poly.Count);
    }
}

/// <summary>Tunable detection thresholds and MVP dimensional assumptions.</summary>
public sealed class AnalyzerOptions
{
    public double WallHeight { get; set; } = 3.0;       // meters
    public double WallThickness { get; set; } = 0.2;    // meters
    public double MinWallLength { get; set; } = 0.1;    // meters
    public double MinRoomArea { get; set; } = 1.0;      // square meters

    public double DoorHeight { get; set; } = 2.1;       // meters
    public double DefaultDoorWidth { get; set; } = 0.9; // meters

    public double WindowSillHeight { get; set; } = 0.9; // meters
    public double WindowHeight { get; set; } = 1.2;     // meters
    public double DefaultWindowWidth { get; set; } = 1.2; // meters
}
