using System.Globalization;
using System.Text;
using InstantRender.Model;

namespace InstantRender.Prompt;

/// <summary>
/// Builds a "super prompt" for Gemini image generation from a <see cref="PlanModel"/>.
///
/// Because Gemini generates an image from text (it does not consume geometry),
/// the prompt's job is to describe the plan precisely enough that the render
/// resembles the actual layout: room list with sizes and adjacency, materials,
/// lighting, and a requested camera. The full plan JSON is also returned so it
/// can be attached as grounding context.
/// </summary>
public sealed class SuperPromptBuilder
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public PromptStyle Style { get; }

    public SuperPromptBuilder(PromptStyle? style = null)
        => Style = style ?? PromptStyle.Default;

    /// <summary>Build the prompt for a specific camera view of the plan.</summary>
    public string Build(PlanModel model, CameraView view)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Generate a photorealistic architectural visualization of a residential floor plan.");
        sb.AppendLine();

        // --- Camera ---------------------------------------------------------
        sb.AppendLine("CAMERA / VIEW:");
        sb.AppendLine(CameraDescription(view, model));
        sb.AppendLine();

        // --- Overall layout -------------------------------------------------
        sb.AppendLine("BUILDING:");
        sb.Append("- A single-storey home, wall height ")
          .Append(model.WallHeight.ToString("0.0", Inv))
          .AppendLine(" meters.");
        if (model.Bounds.Width > 0)
        {
            sb.Append("- Overall footprint approximately ")
              .Append(model.Bounds.Width.ToString("0.0", Inv)).Append(" m x ")
              .Append(model.Bounds.Depth.ToString("0.0", Inv)).AppendLine(" m.");
        }
        sb.Append("- ").Append(model.Walls.Count).Append(" wall segments, ")
          .Append(model.Doors.Count).Append(" doors, ")
          .Append(model.Windows.Count).AppendLine(" windows.");
        sb.AppendLine();

        // --- Rooms ----------------------------------------------------------
        if (model.Rooms.Count > 0)
        {
            sb.AppendLine("ROOMS (name - floor area):");
            foreach (var room in model.Rooms)
            {
                var name = string.IsNullOrWhiteSpace(room.Name) ? "Unnamed room" : room.Name;
                sb.Append("- ").Append(name).Append(" - ")
                  .Append(room.Area.ToString("0.0", Inv)).AppendLine(" m^2.");
            }
            sb.AppendLine();
        }

        // --- Openings -------------------------------------------------------
        if (model.Doors.Count > 0 || model.Windows.Count > 0)
        {
            sb.AppendLine("OPENINGS:");
            if (model.Doors.Count > 0)
                sb.Append("- ").Append(model.Doors.Count)
                  .Append(" doors, ~2.1 m tall, dark wood leaves.").AppendLine();
            if (model.Windows.Count > 0)
                sb.Append("- ").Append(model.Windows.Count)
                  .Append(" windows, sill 0.9 m, ~1.2 m tall, clear glass with thin frames.").AppendLine();
            sb.AppendLine();
        }

        // --- Materials & lighting ------------------------------------------
        sb.AppendLine("MATERIALS:");
        foreach (var m in Style.Materials) sb.Append("- ").AppendLine(m);
        sb.AppendLine();

        sb.AppendLine("LIGHTING:");
        sb.AppendLine("- Bright natural daylight from a sun, plus soft interior fill light.");
        sb.AppendLine("- Realistic soft shadows, global illumination, no harsh blowout.");
        sb.AppendLine();

        // --- Style & output -------------------------------------------------
        sb.Append("STYLE: ").AppendLine(Style.Description);
        sb.AppendLine("OUTPUT: 1920x1080, 16:9, high detail, clean and uncluttered, no text or watermarks, no people.");
        sb.AppendLine("Keep the room layout, proportions, and openings faithful to the description above.");

        return sb.ToString();
    }

    private static string CameraDescription(CameraView view, PlanModel model) => view switch
    {
        CameraView.TopPerspective =>
            "- Elevated 3/4 aerial perspective (dollhouse view) looking down at ~45 degrees, " +
            "ceiling removed so all rooms are visible. Show the whole plan.",
        CameraView.LivingRoom =>
            "- Eye-level interior view standing inside the living room, " +
            "wide-angle lens (~24mm), looking toward the largest window wall.",
        CameraView.Bedroom =>
            "- Eye-level interior view standing inside a bedroom, " +
            "wide-angle lens (~24mm), looking toward the window.",
        _ => "- Elevated 3/4 aerial perspective of the whole plan."
    };

    /// <summary>
    /// Pick the camera views that make sense for this plan: always a top
    /// dollhouse view, a living-room view if a living room is labelled, and a
    /// bedroom view if one is detected.
    /// </summary>
    public IEnumerable<CameraView> SelectViews(PlanModel model)
    {
        yield return CameraView.TopPerspective;

        bool hasLiving = model.Rooms.Any(r => Mentions(r.Name, "living", "majlis", "lounge", "salon"));
        if (hasLiving) yield return CameraView.LivingRoom;

        bool hasBedroom = model.Rooms.Any(r => Mentions(r.Name, "bed", "master", "room"));
        if (hasBedroom) yield return CameraView.Bedroom;
    }

    private static bool Mentions(string? text, params string[] tokens)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (var t in tokens)
            if (text.Contains(t, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}

public enum CameraView { TopPerspective, LivingRoom, Bedroom }

/// <summary>A render style preset (materials + descriptive flavour text).</summary>
public sealed class PromptStyle
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<string> Materials { get; init; }

    public static readonly PromptStyle Default = new()
    {
        Name = "Modern",
        Description = "Clean modern interior design, neutral palette, realistic architectural photography look.",
        Materials = new[]
        {
            "Walls: smooth white plaster, matte.",
            "Floor: light oak wood planks.",
            "Windows: clear glass with slim dark frames.",
            "Doors: dark stained wood.",
            "Ceiling: flat white."
        }
    };

    // Placeholder presets for the future style-presets feature.
    public static readonly PromptStyle Luxury = new()
    {
        Name = "Luxury",
        Description = "High-end luxury interior, warm lighting, marble and brass accents, elegant.",
        Materials = new[]
        {
            "Walls: warm off-white plaster with subtle wainscoting.",
            "Floor: polished marble with veining.",
            "Windows: floor-to-ceiling glass, bronze frames.",
            "Doors: dark walnut with brass handles.",
            "Ceiling: white with recessed cove lighting."
        }
    };
}
