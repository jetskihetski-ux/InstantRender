using System.Text.Json.Serialization;

namespace InstantRender.Model;

/// <summary>
/// The neutral, renderer-agnostic description of a floor plan.
/// This is the "exported scene format" (see docs/scene-format.md). The CAD
/// reader fills it in; the SuperPromptBuilder turns it into a Gemini prompt.
/// All distances are stored in METERS, with X/Y as the plan axes.
/// </summary>
public sealed class PlanModel
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.0";

    [JsonPropertyName("sourceFile")]
    public string? SourceFile { get; set; }

    /// <summary>Default storey height in meters (walls extruded to this).</summary>
    [JsonPropertyName("wallHeight")]
    public double WallHeight { get; set; } = 3.0;

    [JsonPropertyName("walls")]
    public List<Wall> Walls { get; set; } = new();

    [JsonPropertyName("doors")]
    public List<Opening> Doors { get; set; } = new();

    [JsonPropertyName("windows")]
    public List<Opening> Windows { get; set; } = new();

    [JsonPropertyName("rooms")]
    public List<Room> Rooms { get; set; } = new();

    /// <summary>Overall plan bounds in meters, handy for camera framing.</summary>
    [JsonPropertyName("bounds")]
    public BoundingBox Bounds { get; set; } = new();

    public bool IsEmpty => Walls.Count == 0 && Rooms.Count == 0;
}

/// <summary>A 2D point on the plan, in meters.</summary>
public readonly record struct Point2(
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y);

public sealed class BoundingBox
{
    [JsonPropertyName("min")] public Point2 Min { get; set; }
    [JsonPropertyName("max")] public Point2 Max { get; set; }

    [JsonIgnore] public double Width => Max.X - Min.X;
    [JsonIgnore] public double Depth => Max.Y - Min.Y;
}

/// <summary>
/// A wall as a centerline segment plus a thickness. Extruded vertically
/// from 0 to <see cref="PlanModel.WallHeight"/> by the renderer.
/// </summary>
public sealed class Wall
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("start")] public Point2 Start { get; set; }
    [JsonPropertyName("end")] public Point2 End { get; set; }

    /// <summary>Wall thickness in meters (default 0.2 m = 20 cm).</summary>
    [JsonPropertyName("thickness")] public double Thickness { get; set; } = 0.2;

    [JsonIgnore]
    public double Length
    {
        get
        {
            var dx = End.X - Start.X;
            var dy = End.Y - Start.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}

public enum OpeningKind { Door, Window }

/// <summary>
/// A door or window. Positioned by its center point on the plan; the host
/// wall is resolved by proximity. Heights follow the MVP assumptions.
/// </summary>
public sealed class Opening
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("kind")] public OpeningKind Kind { get; set; }
    [JsonPropertyName("center")] public Point2 Center { get; set; }

    /// <summary>Width of the opening along the wall, in meters.</summary>
    [JsonPropertyName("width")] public double Width { get; set; }

    /// <summary>Sill height above floor (0 for doors, 0.9 for windows).</summary>
    [JsonPropertyName("sillHeight")] public double SillHeight { get; set; }

    /// <summary>Height of the opening (2.1 doors, 1.2 windows by default).</summary>
    [JsonPropertyName("height")] public double Height { get; set; }

    /// <summary>Id of the wall this opening is cut into, if resolved.</summary>
    [JsonPropertyName("hostWallId")] public int? HostWallId { get; set; }
}

/// <summary>
/// A closed room boundary. <see cref="Name"/> comes from a nearby text/label
/// when one is found (e.g. "LIVING", "BEDROOM 1").
/// </summary>
public sealed class Room
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }

    /// <summary>Closed polygon outline in meters (CCW preferred).</summary>
    [JsonPropertyName("outline")] public List<Point2> Outline { get; set; } = new();

    /// <summary>Floor area in square meters.</summary>
    [JsonPropertyName("area")] public double Area { get; set; }
}
