using InstantRender.Model;

namespace InstantRender.Cad;

/// <summary>
/// Geometry pulled straight out of the drawing, already converted to meters
/// and bucketed by category, but not yet assembled into walls/rooms/openings.
/// <see cref="PlanAnalyzer"/> turns this into a <see cref="PlanModel"/>.
/// </summary>
public sealed class RawGeometry
{
    /// <summary>Open segments on WALL layers (lines + exploded polyline edges).</summary>
    public List<Segment> WallSegments { get; } = new();

    /// <summary>Closed outlines on WALL/ROOM layers (polygon loops).</summary>
    public List<List<Point2>> ClosedLoops { get; } = new();

    /// <summary>Door markers: center point + approximate width in meters.</summary>
    public List<Marker> DoorMarkers { get; } = new();

    /// <summary>Window markers: center point + approximate width in meters.</summary>
    public List<Marker> WindowMarkers { get; } = new();

    /// <summary>Text labels with their insertion point (for room naming).</summary>
    public List<Label> Labels { get; } = new();
}

public readonly record struct Segment(Point2 Start, Point2 End);

/// <summary>A point-like feature (block/line) with an estimated size.</summary>
public readonly record struct Marker(Point2 Center, double Width);

public readonly record struct Label(Point2 Position, string Text);
