using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace Lobster;

/// <summary>
/// Line icons on a 24×24 grid, drawn with strokes. The core names icons by their Segoe Fluent
/// code point (what Windows uses); this maps the same code points to vector paths, so Linux and
/// macOS need no icon font.
/// </summary>
public static class Icons
{
    private const string Doc = "M6 2 H14 L20 8 V22 H6 Z M14 2 V8 H20";

    private static readonly Dictionary<string, string> Paths = new()
    {
        [""] = "M4 8 H18 M14 4 L18 8 L14 12 M20 16 H6 M10 12 L6 16 L10 20",                       // convert
        [""] = "M8 3 L12 7 L16 3 M8 21 L12 17 L16 21 M4 12 H20",                                   // compress
        [""] = "M4 10 V4 H10 M20 14 V20 H14 M4 4 L10 10 M20 20 L14 14",                            // resize
        [""] = "M6 2 V18 H22 M2 6 H18 V22",                                                         // crop
        [""] = "M20 12 A8 8 0 1 1 15.5 4.8 M16 1 L16.2 5.2 L12 5.6",                               // rotate
        [""] = "M9 6 A3 3 0 1 1 3 6 A3 3 0 1 1 9 6 M9 18 A3 3 0 1 1 3 18 A3 3 0 1 1 9 18 M8.2 7.6 L20 19 M8.2 16.4 L20 5", // cut
        [""] = "M5 3 V8 Q5 13 12 13 Q19 13 19 8 V3 M12 13 V21 M8.5 17.5 L12 21 L15.5 17.5",        // merge
        [""] = "M3 6 H10 L12 8 H21 V19 H3 Z",                                                       // folder / archive
        [""] = "M12 3 L13.6 9.4 L20 11 L13.6 12.6 L12 19 L10.4 12.6 L4 11 L10.4 9.4 Z M19 3 V7 M17 5 H21", // effects
        [""] = "M9 18 V5 L20 3 V16 M9 18 A3 3 0 1 1 3 18 A3 3 0 1 1 9 18 M20 16 A3 3 0 1 1 14 16 A3 3 0 1 1 20 16", // audio
        [""] = Doc,                                                                                 // pages / file
        [""] = "M4 18 A9 9 0 1 1 20 18 M12 15 L16 8 M12 15 A1 1 0 1 1 11.99 15",                   // speed
        [""] = Doc + " M9 13 H17 M9 17 H15",                                                        // document / pdf
        [""] = "M8 8 H20 V20 H8 Z M4 16 V4 H16",                                                    // copy
        [""] = "M3 5 H21 V19 H3 Z M3 16 L9 10 L14 15 L17 12 L21 16 M17 8.5 A1.5 1.5 0 1 1 16.99 8.5", // image
        [""] = "M3 6 H15 V18 H3 Z M15 10 L21 7 V17 L15 14",                                          // video
        [""] = "M4 4 H20 V20 H4 Z M4 10 H20 M4 15 H20 M10 4 V20",                                   // spreadsheet
        [""] = "M3 5 H21 V16 H3 Z M12 16 V20 M8 20 H16",                                            // presentation
        [""] = "M6 6 L18 18 M18 6 L6 18",                                                           // close
        [""] = "M4 12 L10 18 L20 6",                                                                // check
        [""] = "M12 3 L22 20 H2 Z M12 9 V14 M12 17 V17.5",                                          // error / warning
        [""] = "M12 3 A9 9 0 1 1 11.99 3 M12 11 V17 M12 7.5 V8",                                    // info
        [""] = "M12 3 V15 M7 10 L12 15 L17 10 M4 19 H20",                                           // install / download
        [""] = "M5 7 H19 M9 7 V4 H15 V7 M7 7 L8 21 H16 L17 7",                                      // remove
        [""] = "M7 4 L19 12 L7 20 Z",                                                               // play
        [""] = "M7 4 V20 M17 4 V20",                                                                // pause
        [""] = "M3 6 H10 L12 8 H21 V19 H3 Z M12 13 H18 M15 10 L18 13 L15 16",                      // reveal
        [""] = "M14 4 H20 V10 M20 4 L11 13 M18 14 V20 H4 V6 H10",                                   // open
    };

    private static readonly Dictionary<string, Geometry> Cache = new();

    public static Geometry Get(string? glyph)
    {
        glyph ??= "";
        if (Cache.TryGetValue(glyph, out var g)) return g;
        g = Geometry.Parse(Paths.TryGetValue(glyph, out var p) ? p : Doc);
        Cache[glyph] = g;
        return g;
    }

    public static Geometry ByName(string? name) => Get(name switch
    {
        "error" => "",
        "info" => "",
        "video" => "",
        "document" => "",
        "install" => "",
        "remove" => "",
        _ => name
    });

    public static IPen Pen(IBrush brush, double thickness = 1.8) =>
        new Pen(brush, thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

    /// <summary>Draw an icon centered on <paramref name="center"/>.</summary>
    public static void Draw(DrawingContext dc, string? glyph, Point center, double size, IBrush brush)
    {
        double scale = size / 24.0;
        using (dc.PushTransform(Matrix.CreateTranslation(-12, -12) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(center.X, center.Y)))
            dc.DrawGeometry(null, Pen(brush, 1.8), Get(glyph));
    }

    /// <summary>An icon as a control (for dialogs and lists).</summary>
    public static Control View(Geometry geometry, IBrush brush, double size = 20)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(new Path
        {
            Data = geometry, Stroke = brush, StrokeThickness = 1.8,
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round
        });
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }
}
