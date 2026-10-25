using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

using static Lobster.Loc;

namespace Lobster;

/// <summary>PDF work: PDFsharp for structure, Windows' built-in PDF renderer for pixels.</summary>
public interface IPdfRenderer
{
    Task Render(string src, int dpi, CancellationToken ct, Action<int, int, byte[]> onPage);
}

public static class PdfEngine
{
    // ---------------------------------------------------------------- structure

    private static PdfDocument OpenImport(string path)
    {
        try { return PdfReader.Open(path, PdfDocumentOpenMode.Import); }
        catch (Exception ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception(L($"« {Path.GetFileName(path)} » est protégé par mot de passe.", $"“{Path.GetFileName(path)}” is password-protected."));
        }
    }

    public static int PageCount(string path)
    {
        using var d = OpenImport(path);
        return d.PageCount;
    }

    public static void Merge(Job job, CancellationToken ct, IReadOnlyList<string> pdfs, string outp)
    {
        using var output = new PdfDocument();
        int i = 0;
        foreach (var f in pdfs)
        {
            ct.ThrowIfCancellationRequested();
            using var input = OpenImport(f);
            foreach (var page in input.Pages) output.AddPage(page);
            job.Report(0.8 + 0.2 * (++i) / pdfs.Count);
        }
        output.Save(outp);
    }

    public static void Split(Job job, CancellationToken ct, string src)
    {
        using var input = OpenImport(src);
        if (input.PageCount < 2) throw new Exception(L("Ce PDF n'a qu'une page.", "This PDF has only one page."));
        var dir = OutputPaths.ReserveFolder(src, L("pages", "pages"));
        var baseName = Path.GetFileNameWithoutExtension(src);
        int digits = input.PageCount.ToString().Length;
        for (int i = 0; i < input.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            using var one = new PdfDocument();
            one.AddPage(input.Pages[i]);
            one.Save(Path.Combine(dir, $"{baseName} - p{(i + 1).ToString().PadLeft(digits, '0')}.pdf"));
            job.Report((i + 1) / (double)input.PageCount);
        }
        job.AddOutput(dir);
        job.SetStatus(L($"{input.PageCount} pages séparées", $"{input.PageCount} pages split"));
    }

    public static void Select(Job job, CancellationToken ct, string src, IReadOnlyList<int> pages, string suffix)
    {
        var outp = OutputPaths.Reserve(src, "pdf", suffix);
        try
        {
            using var input = OpenImport(src);
            using var output = new PdfDocument();
            foreach (var p in pages)
            {
                ct.ThrowIfCancellationRequested();
                output.AddPage(input.Pages[p]);
            }
            output.Save(outp);
            job.AddOutput(outp);
            job.SetStatus($"{pages.Count} page{(pages.Count > 1 ? "s" : "")}");
        }
        catch { OutputPaths.TryDelete(outp); throw; }
    }

    public static void Rotate(Job job, CancellationToken ct, string src, int degrees)
    {
        var outp = OutputPaths.Reserve(src, "pdf", L("pivoté", "rotated"));
        try
        {
            using var doc = PdfReader.Open(src, PdfDocumentOpenMode.Modify);
            foreach (var page in doc.Pages)
                page.Rotate = ((page.Rotate + degrees) % 360 + 360) % 360;
            doc.Save(outp);
            job.AddOutput(outp);
        }
        catch { OutputPaths.TryDelete(outp); throw; }
    }

    public static void CompressLossless(Job job, CancellationToken ct, string src)
    {
        var outp = OutputPaths.Reserve(src, "pdf", L("compressé", "compressed"));
        try
        {
            using var doc = PdfReader.Open(src, PdfDocumentOpenMode.Modify);
            doc.Options.CompressContentStreams = true;
            doc.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
            doc.Options.NoCompression = false;
            doc.Options.EnableCcittCompressionForBilevelImages = true;
            doc.Save(outp);
            job.AddOutput(outp);
            job.SetStatus(FfmpegEngine.SizeNote(src, outp));
        }
        catch { OutputPaths.TryDelete(outp); throw; }
    }

    /// <summary>Strong compression: Ghostscript when installed, otherwise re-render pages as JPEG.</summary>
    public static async Task CompressStrong(Job job, CancellationToken ct, string src, bool heavy)
    {
        var outp = OutputPaths.Reserve(src, "pdf", L("compressé", "compressed"));
        try
        {
            var gs = Tools.Ghostscript;
            if (gs != null)
            {
                job.SetStatus(L("Ghostscript…", "Ghostscript…"));
                job.Report(-1);
                var r = await ProcessRunner.Run(gs, new[]
                {
                    "-sDEVICE=pdfwrite", "-dCompatibilityLevel=1.5",
                    heavy ? "-dPDFSETTINGS=/screen" : "-dPDFSETTINGS=/ebook",
                    "-dNOPAUSE", "-dQUIET", "-dBATCH", "-dDetectDuplicateImages=true",
                    $"-sOutputFile={outp}", src
                }, ct);
                if (r.ExitCode != 0) throw new Exception("Ghostscript : " + ProcessRunner.Summarize(r.StdErrTail));
            }
            else
            {
                int dpi = heavy ? 96 : 144;
                uint quality = heavy ? 55u : 72u;
                using var col = new MagickImageCollection();
                await RenderPages(src, dpi, ct, (index, count, png) =>
                {
                    var img = new MagickImage(png);
                    img.Alpha(AlphaAction.Remove);
                    img.Format = MagickFormat.Jpeg;
                    img.Quality = quality;
                    img.Density = new Density(dpi, dpi);
                    img.Settings.Compression = CompressionMethod.JPEG;
                    col.Add(img);
                    job.Report(0.9 * (index + 1) / count, L($"Page {index + 1}/{count}", $"Page {index + 1}/{count}"));
                });
                col.Write(outp, MagickFormat.Pdf);
            }
            job.AddOutput(outp);
            job.SetStatus(FfmpegEngine.SizeNote(src, outp) + (gs == null ? L(" · pages en image", " · pages as images") : ""));
        }
        catch { OutputPaths.TryDelete(outp); throw; }
    }

    // ---------------------------------------------------------------- rendering

    /// <summary>Set by the Windows host: the PDF engine built into Windows 10/11.</summary>
    public static IPdfRenderer? NativeRenderer { get; set; }

    /// <summary>Render every page to PNG bytes: native renderer, else Poppler, else Ghostscript.</summary>
    public static async Task RenderPages(string src, int dpi, CancellationToken ct, Action<int, int, byte[]> onPage)
    {
        if (NativeRenderer != null) { await NativeRenderer.Render(src, dpi, ct, onPage); return; }

        var tmp = OutputPaths.TempDir();
        try
        {
            string prefix = Path.Combine(tmp, "p");
            ProcessRunner.Result r;
            if (Tools.Pdftoppm is { } pdftoppm)
                r = await ProcessRunner.Run(pdftoppm, new[] { "-png", "-r", dpi.ToString(), src, prefix }, ct);
            else if (Tools.Ghostscript is { } gs)
                r = await ProcessRunner.Run(gs, new[]
                {
                    "-dSAFER", "-dBATCH", "-dNOPAUSE", "-dQUIET", "-sDEVICE=png16m", "-dTextAlphaBits=4", "-dGraphicsAlphaBits=4",
                    $"-r{dpi}", $"-sOutputFile={prefix}-%04d.png", src
                }, ct);
            else
                throw new ToolMissingException("poppler", L(
                    "Pour transformer un PDF en images, installez poppler-utils : " + Tools.LinuxInstallCommand("poppler-utils", "poppler-utils", "poppler"),
                    "To turn a PDF into images, install poppler-utils: " + Tools.LinuxInstallCommand("poppler-utils", "poppler-utils", "poppler")));

            var pages = Directory.GetFiles(tmp, "p-*.png").OrderBy(f => f, StringComparer.Ordinal).ToList();
            if (pages.Count == 0)
                throw new Exception(L($"Impossible d'ouvrir « {Path.GetFileName(src)} » ({ProcessRunner.Summarize(r.StdErrTail)}).",
                                      $"Cannot open “{Path.GetFileName(src)}” ({ProcessRunner.Summarize(r.StdErrTail)})."));
            // pdftoppm pads numbers to the page count's width; sort numerically to be safe.
            pages = pages.OrderBy(f => int.TryParse(Path.GetFileNameWithoutExtension(f)[2..], out var n) ? n : 0).ToList();
            for (int i = 0; i < pages.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                onPage(i, pages.Count, await File.ReadAllBytesAsync(pages[i], ct));
            }
        }
        finally { OutputPaths.TryDelete(tmp); }
    }

    public static async Task ToImages(Job job, CancellationToken ct, string src, string ext, int dpi = 150, string? nameAfter = null)
    {
        var named = nameAfter ?? src;
        int pages = 0;
        string? dir = null;
        string baseName = Path.GetFileNameWithoutExtension(named);
        var outputs = new List<string>();
        try
        {
            await RenderPages(src, dpi, ct, (index, count, png) =>
            {
                pages = count;
                string outp;
                if (count == 1)
                    outp = OutputPaths.Reserve(named, ext);
                else
                {
                    dir ??= OutputPaths.ReserveFolder(named, L("images", "images"));
                    outp = Path.Combine(dir, $"{baseName} - p{(index + 1).ToString().PadLeft(count.ToString().Length, '0')}.{ext}");
                }
                outputs.Add(outp);
                if (ext == "png") File.WriteAllBytes(outp, png);
                else
                {
                    using var img = new MagickImage(png);
                    img.Alpha(AlphaAction.Remove);
                    img.Quality = 90;
                    img.Write(outp, ImageEngine.FormatOf(ext));
                }
                job.Report((index + 1) / (double)count, L($"Page {index + 1}/{count}", $"Page {index + 1}/{count}"));
            });
        }
        catch
        {
            if (dir != null) OutputPaths.TryDelete(dir);
            else foreach (var o in outputs) OutputPaths.TryDelete(o);
            throw;
        }
        job.AddOutput(dir ?? outputs.FirstOrDefault() ?? named);
        job.SetStatus(pages > 1 ? $"{pages} images" : L("Terminé", "Done"));
    }

    public static void ToText(Job job, CancellationToken ct, string src)
    {
        var outp = OutputPaths.Reserve(src, "txt");
        try
        {
            var sb = new StringBuilder();
            using (var doc = UglyToad.PdfPig.PdfDocument.Open(src))
            {
                int n = doc.NumberOfPages, i = 0;
                foreach (var page in doc.GetPages())
                {
                    ct.ThrowIfCancellationRequested();
                    sb.AppendLine(UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(page));
                    sb.AppendLine();
                    job.Report(++i / (double)n);
                }
            }
            File.WriteAllText(outp, sb.ToString(), new UTF8Encoding(true));
            job.AddOutput(outp);
            if (sb.ToString().Trim().Length == 0) job.SetStatus(L("Aucun texte trouvé (PDF scanné ?)", "No text found (scanned PDF?)"));
        }
        catch { OutputPaths.TryDelete(outp); throw; }
    }

    // ---------------------------------------------------------------- page ranges

    /// <summary>Parse "1-3, 5, 8-" into zero-based page indexes.</summary>
    public static List<int> ParseRange(string text, int pageCount)
    {
        var result = new SortedSet<int>();
        foreach (var raw in text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.Trim();
            int a, b;
            if (part.Contains('-'))
            {
                var bits = part.Split('-');
                a = string.IsNullOrWhiteSpace(bits[0]) ? 1 : int.Parse(bits[0]);
                b = string.IsNullOrWhiteSpace(bits[1]) ? pageCount : int.Parse(bits[1]);
            }
            else a = b = int.Parse(part);
            if (a > b) (a, b) = (b, a);
            if (a < 1 || b > pageCount) throw new FormatException(L($"Pages 1 à {pageCount} seulement.", $"Pages 1 to {pageCount} only."));
            for (int p = a; p <= b; p++) result.Add(p - 1);
        }
        if (result.Count == 0) throw new FormatException(L("Aucune page indiquée.", "No pages given."));
        return result.ToList();
    }
}
