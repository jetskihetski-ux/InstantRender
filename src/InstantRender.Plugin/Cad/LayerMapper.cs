namespace InstantRender.Cad;

/// <summary>The semantic role a CAD layer plays in the floor plan.</summary>
public enum PlanCategory
{
    Unknown = 0,
    Wall,
    Door,
    Window,
    Room,
    Annotation
}

/// <summary>
/// Maps raw AutoCAD layer names to a <see cref="PlanCategory"/>.
///
/// It seeds sensible defaults for the common AEC layer conventions
/// (plain, AIA "A-" prefixed, etc.) and lets the caller override the mapping
/// per layer when a drawing uses non-standard names. The override path backs
/// the "map layers manually" requirement.
/// </summary>
public sealed class LayerMapper
{
    // Substrings checked case-insensitively against the layer name.
    private static readonly (string Token, PlanCategory Category)[] DefaultRules =
    {
        ("A-GLAZ", PlanCategory.Window),
        ("GLAZ",   PlanCategory.Window),
        ("WINDOW", PlanCategory.Window),
        ("A-WALL", PlanCategory.Wall),
        ("WALL",   PlanCategory.Wall),
        ("A-DOOR", PlanCategory.Door),
        ("DOOR",   PlanCategory.Door),
        ("ROOM",   PlanCategory.Room),
        ("A-AREA", PlanCategory.Room),
        ("ANNO",   PlanCategory.Annotation),
        ("TEXT",   PlanCategory.Annotation),
    };

    // Explicit per-layer overrides set by the user (case-insensitive key).
    private readonly Dictionary<string, PlanCategory> _overrides =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Force a specific layer to a category (manual mapping UI).</summary>
    public void Override(string layerName, PlanCategory category)
        => _overrides[layerName] = category;

    public IReadOnlyDictionary<string, PlanCategory> Overrides => _overrides;

    /// <summary>Resolve a layer name to its plan category.</summary>
    public PlanCategory Classify(string layerName)
    {
        if (string.IsNullOrWhiteSpace(layerName))
            return PlanCategory.Unknown;

        if (_overrides.TryGetValue(layerName, out var explicitCat))
            return explicitCat;

        // Order matters: more specific tokens (A-WALL, A-GLAZ) come first.
        foreach (var (token, category) in DefaultRules)
        {
            if (layerName.Contains(token, StringComparison.OrdinalIgnoreCase))
                return category;
        }

        return PlanCategory.Unknown;
    }
}
