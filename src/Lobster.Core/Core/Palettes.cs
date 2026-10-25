using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Lobster;

/// <summary>UI-framework-neutral color (WPF and Avalonia convert it).</summary>
public readonly record struct Rgba(byte A, byte R, byte G, byte B)
{
    public static Rgba Hex(string hex, byte alpha = 0xFF)
    {
        var t = hex.TrimStart('#');
        return new Rgba(alpha,
            byte.Parse(t[..2], NumberStyles.HexNumber),
            byte.Parse(t[2..4], NumberStyles.HexNumber),
            byte.Parse(t[4..6], NumberStyles.HexNumber));
    }

    public Rgba WithAlpha(byte a) => this with { A = a };
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
    public double Luminance => 0.2126 * R + 0.7152 * G + 0.0722 * B;

    public static Rgba Blend(Rgba a, Rgba b, double t) => new(
        a.A,
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));
}

/// <summary>Colors of the radial menu.</summary>
public sealed record RadialPalette(
    string Id, string NameFr, string NameEn,
    Rgba Accent,     // hovered segment, hub ring, open category outline
    Rgba OnAccent,   // text drawn on the accent
    Rgba Segment,    // inner ring
    Rgba Outer,      // outer ring
    Rgba Open,       // inner segment whose children are shown
    Rgba Hub,
    Rgba Line,
    Rgba Text,
    Rgba TextDim,
    Rgba Shadow)
{
    public string Name => Loc.L(NameFr, NameEn);
}

public static class Palettes
{
    private static RadialPalette Dark(string id, string fr, string en, string accent, string onAccent = "#FFFFFF")
    {
        var a = Rgba.Hex(accent);
        return new(id, fr, en, a, Rgba.Hex(onAccent),
            Segment: Rgba.Hex("#1D1D21", 0xF0), Outer: Rgba.Hex("#26262B", 0xF4),
            Open: Rgba.Blend(Rgba.Hex("#1D1D21", 0xF2), a, 0.22),
            Hub: Rgba.Hex("#141416", 0xF7), Line: Rgba.Hex("#FFFFFF", 0x26),
            Text: Rgba.Hex("#F4F4F5"), TextDim: Rgba.Hex("#A1A1AA"), Shadow: Rgba.Hex("#000000", 0x60));
    }

    public static readonly IReadOnlyList<RadialPalette> All = new List<RadialPalette>
    {
        Dark("lobster",  "Homard",    "Lobster",  "#E8452C"),
        Dark("ocean",    "Océan",     "Ocean",    "#2F8FE8"),
        Dark("forest",   "Forêt",     "Forest",   "#2FBF71"),
        Dark("amethyst", "Améthyste", "Amethyst", "#9B6CF0"),
        Dark("amber",    "Ambre",     "Amber",    "#F2A12B", "#1A1206"),
        Dark("rose",     "Rose",      "Rose",     "#EC4F8A"),
        Dark("graphite", "Graphite",  "Graphite", "#E4E4E7", "#141416"),
        new("light", "Clair", "Light", Rgba.Hex("#E8452C"), Rgba.Hex("#FFFFFF"),
            Segment: Rgba.Hex("#FFFFFF", 0xF4), Outer: Rgba.Hex("#F1F1F4", 0xF7), Open: Rgba.Hex("#FDE8E4", 0xF4),
            Hub: Rgba.Hex("#FFFFFF", 0xFA), Line: Rgba.Hex("#000000", 0x30),
            Text: Rgba.Hex("#1D1D21"), TextDim: Rgba.Hex("#6B6B76"), Shadow: Rgba.Hex("#000000", 0x40)),
    };

    /// <summary>Palette chosen in settings; "custom" builds a dark palette around the custom accent.</summary>
    public static RadialPalette Current
    {
        get
        {
            var s = AppState.Settings;
            if (s.Palette == "custom" && TryParse(s.CustomAccent, out var accent))
                return Custom(accent);
            return All.FirstOrDefault(p => p.Id == s.Palette) ?? All[0];
        }
    }

    public static RadialPalette Custom(Rgba accent) =>
        // Dark text on light accents, white on dark ones.
        Dark("custom", "Personnalisée", "Custom", accent.ToHex(), accent.Luminance > 160 ? "#141416" : "#FFFFFF");

    public static bool TryParse(string? text, out Rgba color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().TrimStart('#');
        if (t.Length != 6 || !int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)) return false;
        color = Rgba.Hex(t);
        return true;
    }
}
