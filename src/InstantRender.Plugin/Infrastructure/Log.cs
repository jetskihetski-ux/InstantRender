using Autodesk.AutoCAD.ApplicationServices.Core;

namespace InstantRender.Infrastructure;

/// <summary>Writes messages to the AutoCAD command-line editor.</summary>
public static class Log
{
    public static void Info(string message) => Write($"\n[Instant Render] {message}");
    public static void Warn(string message) => Write($"\n[Instant Render] WARNING: {message}");
    public static void Error(string message) => Write($"\n[Instant Render] ERROR: {message}");

    private static void Write(string text)
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        doc?.Editor.WriteMessage(text);
    }
}
