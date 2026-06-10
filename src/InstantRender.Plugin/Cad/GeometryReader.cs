using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using InstantRender.Model;

namespace InstantRender.Cad;

/// <summary>
/// Reads CAD entities (lines, polylines, arcs, blocks, text) from a selection
/// or from model space, classifies them by layer, converts coordinates to
/// meters, and returns a <see cref="RawGeometry"/> bucket.
///
/// NOTE: This file depends on the AutoCAD .NET API and only compiles/runs
/// inside AutoCAD. See docs/setup.md.
/// </summary>
public sealed class GeometryReader
{
    private readonly LayerMapper _layers;
    private readonly double _toMeters; // multiply drawing units by this -> meters

    public GeometryReader(Database db, LayerMapper layers)
    {
        _layers = layers;
        _toMeters = UnitsToMeters(db.Insunits);
    }

    /// <summary>
    /// Prompt the user to select objects; an empty selection falls back to the
    /// entire model space (the "visible geometry" path).
    /// </summary>
    public RawGeometry Read(Document doc)
    {
        var ed = doc.Editor;
        var db = doc.Database;
        var raw = new RawGeometry();

        var ids = PromptForSelection(ed);

        using var tr = db.TransactionManager.StartTransaction();
        foreach (var id in ids)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent)
                continue;

            var category = _layers.Classify(ent.Layer);
            if (category == PlanCategory.Unknown)
                continue;

            ReadEntity(ent, category, raw, tr);
        }
        tr.Commit();

        return raw;
    }

    private ObjectId[] PromptForSelection(Editor ed)
    {
        var sel = ed.SelectImplied(); // honor objects pre-selected by the user
        if (sel.Status == PromptStatus.OK && sel.Value.Count > 0)
            return sel.Value.GetObjectIds();

        var opts = new PromptSelectionOptions
        {
            MessageForAdding = "\nSelect floor plan objects (Enter for entire drawing): "
        };
        sel = ed.GetSelection(opts);
        if (sel.Status == PromptStatus.OK)
            return sel.Value.GetObjectIds();

        // Nothing picked -> select everything in model space.
        return ed.SelectAll().Value?.GetObjectIds() ?? Array.Empty<ObjectId>();
    }

    private void ReadEntity(Entity ent, PlanCategory category, RawGeometry raw, Transaction tr)
    {
        switch (ent)
        {
            case Line line:
                AddSegment(raw, category, ToM(line.StartPoint), ToM(line.EndPoint));
                break;

            case Polyline pline:
                ReadPolyline(pline, category, raw);
                break;

            case Arc arc:
                ReadArc(arc, category, raw);
                break;

            case BlockReference block:
                ReadBlock(block, category, raw);
                break;

            case DBText text:
                if (category is PlanCategory.Annotation or PlanCategory.Room)
                    raw.Labels.Add(new Label(ToM(text.Position), text.TextString));
                break;

            case MText mtext:
                if (category is PlanCategory.Annotation or PlanCategory.Room)
                    raw.Labels.Add(new Label(ToM(mtext.Location), mtext.Text));
                break;
        }
    }

    private void ReadPolyline(Polyline pline, PlanCategory category, RawGeometry raw)
    {
        var pts = new List<Point2>(pline.NumberOfVertices);
        for (int i = 0; i < pline.NumberOfVertices; i++)
        {
            var p = pline.GetPoint2dAt(i);
            pts.Add(new Point2(p.X * _toMeters, p.Y * _toMeters));
        }
        if (pts.Count < 2) return;

        bool closed = pline.Closed || (pts.Count > 2 && Near(pts[0], pts[^1]));

        if (closed && category is PlanCategory.Room or PlanCategory.Wall)
        {
            raw.ClosedLoops.Add(pts);
        }
        else if (category == PlanCategory.Wall)
        {
            for (int i = 0; i < pts.Count - 1; i++)
                raw.WallSegments.Add(new Segment(pts[i], pts[i + 1]));
        }
        else if (category is PlanCategory.Door or PlanCategory.Window)
        {
            // Treat the polyline's bounding box as the opening footprint.
            AddMarkerFromPoints(raw, category, pts);
        }
    }

    private void ReadArc(Arc arc, PlanCategory category, RawGeometry raw)
    {
        // Door swings are arcs on the DOOR layer -> use the arc as a door marker.
        if (category != PlanCategory.Door) return;
        var center = ToM(arc.Center);
        var width = arc.Radius * _toMeters; // swing radius ~= leaf width
        raw.DoorMarkers.Add(new Marker(center, width));
    }

    private void ReadBlock(BlockReference block, PlanCategory category, RawGeometry raw)
    {
        var center = ToM(block.Position);
        // Estimate width from the block's geometric extents when available.
        double width = category == PlanCategory.Window ? 1.2 : 0.9;
        try
        {
            var ext = block.GeometricExtents;
            width = (ext.MaxPoint.X - ext.MinPoint.X) * _toMeters;
            center = new Point2(
                (ext.MinPoint.X + ext.MaxPoint.X) * 0.5 * _toMeters,
                (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5 * _toMeters);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            // Some blocks have no extents; fall back to defaults above.
        }

        if (category == PlanCategory.Door)
            raw.DoorMarkers.Add(new Marker(center, width));
        else if (category == PlanCategory.Window)
            raw.WindowMarkers.Add(new Marker(center, width));
    }

    private void AddSegment(RawGeometry raw, PlanCategory category, Point2 a, Point2 b)
    {
        if (category == PlanCategory.Wall)
            raw.WallSegments.Add(new Segment(a, b));
    }

    private static void AddMarkerFromPoints(RawGeometry raw, PlanCategory category, List<Point2> pts)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in pts)
        {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }
        var center = new Point2((minX + maxX) * 0.5, (minY + maxY) * 0.5);
        var width = Math.Max(maxX - minX, maxY - minY);
        var marker = new Marker(center, width);
        if (category == PlanCategory.Door) raw.DoorMarkers.Add(marker);
        else if (category == PlanCategory.Window) raw.WindowMarkers.Add(marker);
    }

    private Point2 ToM(Point3d p) => new(p.X * _toMeters, p.Y * _toMeters);

    private static bool Near(Point2 a, Point2 b)
        => Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6;

    /// <summary>Conversion factor from the drawing's INSUNITS to meters.</summary>
    private static double UnitsToMeters(UnitsValue units) => units switch
    {
        UnitsValue.Millimeters => 0.001,
        UnitsValue.Centimeters => 0.01,
        UnitsValue.Meters      => 1.0,
        UnitsValue.Inches      => 0.0254,
        UnitsValue.Feet        => 0.3048,
        // Unitless drawings are assumed to be in millimeters (AEC default).
        _ => 0.001
    };
}
