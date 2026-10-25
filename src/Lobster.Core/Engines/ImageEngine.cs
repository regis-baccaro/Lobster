using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ImageMagick;

using static Lobster.Loc;

namespace Lobster;

/// <summary>Image work through the embedded ImageMagick (Magick.NET). No external tools.</summary>
public static class ImageEngine
{
    public static readonly string[] Targets = { "png", "jpg", "webp", "avif", "gif", "bmp", "tiff", "ico", "pdf" };

    public static MagickFormat FormatOf(string ext) => ext switch
    {
        "png" => MagickFormat.Png,
        "jpg" or "jpeg" => MagickFormat.Jpeg,
        "webp" => MagickFormat.WebP,
        "avif" => MagickFormat.Avif,
        "gif" => MagickFormat.Gif,
        "bmp" => MagickFormat.Bmp,
        "tiff" or "tif" => MagickFormat.Tiff,
        "ico" => MagickFormat.Icon,
        "pdf" => MagickFormat.Pdf,
        _ => MagickFormat.Png
    };

    public static MagickImage Load(string path)
    {
        var ext = FileKinds.Ext(path);
        var settings = new MagickReadSettings();
        if (ext == "svg")
        {
            settings.Density = new Density(192);
            settings.BackgroundColor = MagickColors.Transparent;
        }
        var img = new MagickImage(path, settings);
        img.AutoOrient();
        return img;
    }

    private static bool IsAnimated(string path) => FileKinds.Ext(path) is "gif" or "webp";

    /// <summary>Prepare an image for a given output format (alpha, quality, size limits).</summary>
    private static void PrepareFor(MagickImage img, string ext, uint quality = 0)
    {
        switch (ext)
        {
            case "jpg":
            case "jpeg":
            case "bmp":
                if (img.HasAlpha)
                {
                    img.BackgroundColor = MagickColors.White;
                    img.Alpha(AlphaAction.Remove);
                }
                if (ext != "bmp") img.Quality = quality > 0 ? quality : 90;
                break;
            case "webp":
                img.Quality = quality > 0 ? quality : 85;
                break;
            case "avif":
                img.Quality = quality > 0 ? quality : 65;
                break;
            case "ico":
                if (img.Width > 256 || img.Height > 256) img.Resize(new MagickGeometry(256, 256));
                var sizes = new[] { 256u, 128u, 64u, 48u, 32u, 24u, 16u }.Where(s => s <= Math.Max(img.Width, img.Height));
                img.Settings.SetDefine(MagickFormat.Icon, "auto-resize", string.Join(",", sizes));
                break;
            case "tiff":
                img.Settings.Compression = CompressionMethod.LZW;
                break;
            case "pdf":
                if (!img.HasAlpha)
                {
                    img.Settings.Compression = CompressionMethod.JPEG;
                    img.Quality = 90;
                }
                break;
            case "png":
                img.Settings.SetDefine(MagickFormat.Png, "compression-level", "9");
                break;
        }
    }

    private static void Save(MagickImage img, string outp, string ext)
    {
        img.Write(outp, FormatOf(ext));
    }

    public static void Convert(Job job, CancellationToken ct, string src, string ext)
    {
        var outp = OutputPaths.Reserve(src, ext);
        try
        {
            ct.ThrowIfCancellationRequested();
            if (IsAnimated(src) && ext is "gif" or "webp")
            {
                using var col = new MagickImageCollection(src);
                if (col.Count > 1)
                {
                    col.Coalesce();
                    if (ext == "webp") foreach (var f in col) f.Quality = 85;
                    if (ext == "gif") col.Optimize();
                    col.Write(outp, FormatOf(ext));
                    job.AddOutput(outp);
                    job.SetStatus(FfmpegEngine.SizeNote(src, outp));
                    return;
                }
            }
            using var img = Load(src);
            PrepareFor(img, ext);
            Save(img, outp, ext);
            job.AddOutput(outp);
            job.SetStatus(FfmpegEngine.SizeNote(src, outp));
        }
        catch
        {
            OutputPaths.TryDelete(outp);
            throw;
        }
    }

    /// <summary>Generic edit: load, mutate, write next to source with a suffix.</summary>
    public static void Edit(Job job, CancellationToken ct, string src, string suffix, Action<MagickImage> edit, string? forceExt = null)
    {
        var srcExt = FileKinds.Ext(src);
        var ext = forceExt ?? (srcExt is "png" or "jpg" or "jpeg" or "webp" or "avif" or "gif" or "bmp" or "tiff" or "tif" ? srcExt : "png");
        if (ext == "jpeg") ext = "jpg";
        if (ext == "tif") ext = "tiff";
        var outp = OutputPaths.Reserve(src, ext, suffix);
        try
        {
            ct.ThrowIfCancellationRequested();
            using var img = Load(src);
            var q = img.Quality;
            edit(img);
            PrepareFor(img, ext, q is > 0 and <= 100 ? Math.Max(q, 85) : 0);
            Save(img, outp, ext);
            job.AddOutput(outp);
            job.SetStatus(FfmpegEngine.SizeNote(src, outp));
        }
        catch
        {
            OutputPaths.TryDelete(outp);
            throw;
        }
    }

    public static void Compress(Job job, CancellationToken ct, string src, int level)
    {
        var srcExt = FileKinds.Ext(src);
        string ext = srcExt switch
        {
            "png" or "gif" => "png",
            "webp" => "webp",
            "avif" => "avif",
            _ => "jpg"
        };
        var outp = OutputPaths.Reserve(src, ext, L("compressé", "compressed"));
        try
        {
            ct.ThrowIfCancellationRequested();
            using var img = Load(src);
            if (ext == "jpg" && img.HasAlpha && srcExt != "jpg" && srcExt != "jpeg")
            {
                // Transparent source: keep transparency rather than flattening onto white.
                OutputPaths.TryDelete(outp);
                ext = "png";
                outp = OutputPaths.Reserve(src, ext, L("compressé", "compressed"));
            }
            img.Strip();
            uint maxSide = level switch { 3 => 2560u, _ => 0u };
            if (maxSide > 0 && Math.Max(img.Width, img.Height) > maxSide)
                img.Resize(new MagickGeometry(maxSide, maxSide) { Greater = true });

            if (ext == "png")
            {
                img.Settings.SetDefine(MagickFormat.Png, "compression-level", "9");
                if (level >= 2) img.Quantize(new QuantizeSettings { Colors = level == 2 ? 256u : 128u, DitherMethod = DitherMethod.FloydSteinberg });
            }
            else
            {
                img.Quality = level switch { 1 => 82u, 2 => 68u, _ => 50u };
                if (ext == "jpg") img.Settings.SetDefine(MagickFormat.Jpeg, "sampling-factor", "4:2:0");
                if (img.HasAlpha && ext == "jpg") { img.BackgroundColor = MagickColors.White; img.Alpha(AlphaAction.Remove); }
            }
            Save(img, outp, ext);
            job.AddOutput(outp);
            job.SetStatus(FfmpegEngine.SizeNote(src, outp));
        }
        catch
        {
            OutputPaths.TryDelete(outp);
            throw;
        }
    }

    public static void ForWeb(Job job, CancellationToken ct, string src)
    {
        var outp = OutputPaths.Reserve(src, "webp", L("web", "web"));
        try
        {
            using var img = Load(src);
            img.Strip();
            img.Resize(new MagickGeometry(1920, 1920) { Greater = true });
            img.Quality = 80;
            img.Write(outp, MagickFormat.WebP);
            job.AddOutput(outp);
            job.SetStatus(FfmpegEngine.SizeNote(src, outp));
        }
        catch { OutputPaths.TryDelete(outp); throw; }
    }

    public static void ResizePercent(Job job, CancellationToken ct, string src, int percent) =>
        Edit(job, ct, src, $"{percent} %", img => img.Resize(new Percentage(percent)));

    public static void ResizeMax(Job job, CancellationToken ct, string src, uint maxSide) =>
        Edit(job, ct, src, $"{maxSide} px", img =>
        {
            if (Math.Max(img.Width, img.Height) > maxSide)
                img.Resize(new MagickGeometry(maxSide, maxSide) { Greater = true });
        });

    public static void Crop(Job job, CancellationToken ct, string src, double fx, double fy, double fw, double fh) =>
        Edit(job, ct, src, L("rogné", "cropped"), img =>
        {
            int x = (int)Math.Round(fx * img.Width), y = (int)Math.Round(fy * img.Height);
            uint w = (uint)Math.Max(1, Math.Round(fw * img.Width)), h = (uint)Math.Max(1, Math.Round(fh * img.Height));
            // Square-ish selections (locked 1:1) must come out exactly square.
            if (Math.Abs((double)w - h) <= 1 && Math.Abs(fw * img.Width - fh * img.Height) < 1) h = w;
            img.Crop(new MagickGeometry(x, y, w, h));
            img.ResetPage();
        });

    /// <summary>Downscaled PNG used as preview in the crop dialog.</summary>
    public static (string path, int width, int height) Preview(string src)
    {
        var tmp = Path.Combine(OutputPaths.TempDir(), "preview.png");
        using var img = Load(src);
        int w = (int)img.Width, h = (int)img.Height;
        img.Resize(new MagickGeometry(1600, 1600) { Greater = true });
        img.Write(tmp, MagickFormat.Png);
        return (tmp, w, h);
    }

    public static void MergeToPdf(Job job, CancellationToken ct, IReadOnlyList<string> images, string outp)
    {
        using var col = new MagickImageCollection();
        int i = 0;
        foreach (var f in images)
        {
            ct.ThrowIfCancellationRequested();
            var img = Load(f);
            PrepareFor(img, "pdf");
            col.Add(img);
            job.Report(++i / (double)images.Count * 0.8);
        }
        col.Write(outp, MagickFormat.Pdf);
    }

    public static void MergeGif(Job job, CancellationToken ct, IReadOnlyList<string> images)
    {
        var outp = OutputPaths.Reserve(images[0], "gif", L("animation", "animation"));
        try
        {
            using var col = new MagickImageCollection();
            uint w = 0, h = 0;
            foreach (var f in images)
            {
                ct.ThrowIfCancellationRequested();
                var img = Load(f);
                if (w == 0)
                {
                    if (Math.Max(img.Width, img.Height) > 800) img.Resize(new MagickGeometry(800, 800));
                    w = img.Width; h = img.Height;
                }
                else
                {
                    img.Resize(new MagickGeometry(w, h));
                    img.Extent(w, h, Gravity.Center, MagickColors.White);
                }
                img.AnimationDelay = 80;
                col.Add(img);
            }
            col.Quantize(new QuantizeSettings { Colors = 256 });
            col.Optimize();
            col.Write(outp, MagickFormat.Gif);
            job.AddOutput(outp);
        }
        catch { OutputPaths.TryDelete(outp); throw; }
    }

    public static void Append(Job job, CancellationToken ct, IReadOnlyList<string> images, bool horizontal)
    {
        var ext = FileKinds.Ext(images[0]) is "jpg" or "jpeg" ? "jpg" : "png";
        var outp = OutputPaths.Reserve(images[0], ext, horizontal ? L("côte à côte", "side by side") : L("empilées", "stacked"));
        try
        {
            using var col = new MagickImageCollection();
            uint target = 0;
            foreach (var f in images)
            {
                ct.ThrowIfCancellationRequested();
                var img = Load(f);
                if (target == 0) target = Math.Min(horizontal ? img.Height : img.Width, 4000u);
                if (horizontal) img.Resize(new MagickGeometry(0, target));
                else img.Resize(new MagickGeometry(target, 0));
                col.Add(img);
            }
            using var result = horizontal ? col.AppendHorizontally() : col.AppendVertically();
            var mi = (MagickImage)result;
            PrepareFor(mi, ext);
            mi.Write(outp, FormatOf(ext));
            job.AddOutput(outp);
        }
        catch { OutputPaths.TryDelete(outp); throw; }
    }
}
