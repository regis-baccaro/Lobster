using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Markdig;

using static Lobster.Loc;

namespace Lobster;

/// <summary>Multi-engine operations: documents, Markdown, archives, mixed merges.</summary>
public static class Pipelines
{
    public static async Task ConvertDocument(Job job, CancellationToken ct, string src, string ext)
    {
        var outp = OutputPaths.Reserve(src, ext);
        try
        {
            job.Report(-1, Tools.HasWord || Tools.HasExcel || Tools.HasPowerPoint ? L("Office…", "Office…") : L("LibreOffice…", "LibreOffice…"));
            await OfficeEngine.Convert(src, ext, outp, ct);
            job.AddOutput(outp);
            job.SetStatus(L("Terminé", "Done"));
        }
        catch { OutputPaths.TryDelete(outp); throw; }
    }

    public static async Task PresentationToImages(Job job, CancellationToken ct, string src)
    {
        var tmp = Path.Combine(OutputPaths.TempDir(), Path.GetFileNameWithoutExtension(src) + ".pdf");
        job.Report(-1, L("Export PDF…", "Exporting PDF…"));
        await OfficeEngine.Convert(src, "pdf", tmp, ct);
        try { await PdfEngine.ToImages(job, ct, tmp, "png", 150, nameAfter: src); }
        finally { OutputPaths.TryDelete(Path.GetDirectoryName(tmp)); }
    }

    // ---------------------------------------------------------------- Markdown

    private static string MarkdownToHtml(string src)
    {
        var md = File.ReadAllText(src);
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        var body = Markdown.ToHtml(md, pipeline);
        var title = System.Net.WebUtility.HtmlEncode(Path.GetFileNameWithoutExtension(src));
        var baseHref = new Uri(Path.GetDirectoryName(Path.GetFullPath(src))! + Path.DirectorySeparatorChar).AbsoluteUri;
        return $$"""
            <!doctype html>
            <html lang="fr"><head><meta charset="utf-8"><title>{{title}}</title>
            <base href="{{baseHref}}">
            <style>
            body{font-family:"Segoe UI",system-ui,sans-serif;max-width:46em;margin:2.5em auto;padding:0 1.2em;line-height:1.6;color:#1d1d1f}
            h1,h2,h3{line-height:1.25} h1{font-size:2em} h2{border-bottom:1px solid #e5e5e5;padding-bottom:.2em}
            code{font-family:Consolas,monospace;background:#f4f4f5;padding:.1em .35em;border-radius:4px}
            pre{background:#f4f4f5;padding:1em;border-radius:8px;overflow:auto} pre code{background:none;padding:0}
            table{border-collapse:collapse} th,td{border:1px solid #ddd;padding:.4em .7em} blockquote{border-left:4px solid #e8452c;margin:0;padding-left:1em;color:#555}
            img{max-width:100%}
            </style></head><body>
            {{body}}
            </body></html>
            """;
    }

    public static async Task ConvertMarkdown(Job job, CancellationToken ct, string src, string ext)
    {
        var outp = OutputPaths.Reserve(src, ext);
        try
        {
            var html = MarkdownToHtml(src);
            if (ext == "html")
            {
                File.WriteAllText(outp, html, new UTF8Encoding(false));
            }
            else
            {
                var tmp = Path.Combine(OutputPaths.TempDir(), Path.GetFileNameWithoutExtension(src) + ".html");
                File.WriteAllText(tmp, html, new UTF8Encoding(false));
                job.Report(-1, L("Mise en page…", "Laying out…"));
                await OfficeEngine.Convert(tmp, ext, outp, ct);
            }
            job.AddOutput(outp);
        }
        catch { OutputPaths.TryDelete(outp); throw; }
    }

    // ---------------------------------------------------------------- merge anything into one PDF

    public static async Task MergeToPdf(Job job, CancellationToken ct, IReadOnlyList<string> files)
    {
        var outp = OutputPaths.Reserve(files[0], "pdf", L("fusion", "merged"));
        var tmpDir = OutputPaths.TempDir();
        try
        {
            var parts = new List<string>();
            var imageRun = new List<string>();

            void FlushImages()
            {
                if (imageRun.Count == 0) return;
                var p = Path.Combine(tmpDir, $"part{parts.Count:000}.pdf");
                ImageEngine.MergeToPdf(job, ct, imageRun.ToList(), p);
                parts.Add(p);
                imageRun.Clear();
            }

            int i = 0;
            foreach (var f in files)
            {
                ct.ThrowIfCancellationRequested();
                job.Report(0.8 * i++ / files.Count, L($"Préparation {i}/{files.Count}", $"Preparing {i}/{files.Count}"));
                var kind = FileKinds.Of(f);
                switch (kind)
                {
                    case FileKind.Image:
                        imageRun.Add(f);
                        break;
                    case FileKind.Pdf:
                        FlushImages();
                        parts.Add(f);
                        break;
                    case FileKind.Markdown:
                    {
                        FlushImages();
                        var html = Path.Combine(tmpDir, $"md{parts.Count:000}.html");
                        File.WriteAllText(html, MarkdownToHtml(f), new UTF8Encoding(false));
                        var p = Path.Combine(tmpDir, $"part{parts.Count:000}.pdf");
                        await OfficeEngine.Convert(html, "pdf", p, ct);
                        parts.Add(p);
                        break;
                    }
                    case FileKind.Document or FileKind.Spreadsheet or FileKind.Presentation:
                    {
                        FlushImages();
                        var p = Path.Combine(tmpDir, $"part{parts.Count:000}.pdf");
                        await OfficeEngine.Convert(f, "pdf", p, ct);
                        parts.Add(p);
                        break;
                    }
                    default:
                        throw new Exception(L($"« {Path.GetFileName(f)} » ne peut pas aller dans un PDF.", $"“{Path.GetFileName(f)}” cannot go into a PDF."));
                }
            }
            FlushImages();
            job.SetStatus(L("Assemblage…", "Assembling…"));
            PdfEngine.Merge(job, ct, parts, outp);
            job.AddOutput(outp);
            job.SetStatus(L($"{files.Count} fichiers → 1 PDF", $"{files.Count} files → 1 PDF"));
        }
        catch { OutputPaths.TryDelete(outp); throw; }
        finally { OutputPaths.TryDelete(tmpDir); }
    }

    public static async Task ToPdf(Job job, CancellationToken ct, string src)
    {
        switch (FileKinds.Of(src))
        {
            case FileKind.Image: ImageEngine.Convert(job, ct, src, "pdf"); break;
            case FileKind.Markdown: await ConvertMarkdown(job, ct, src, "pdf"); break;
            default: await ConvertDocument(job, ct, src, "pdf"); break;
        }
    }

    // ---------------------------------------------------------------- ZIP

    public static void Zip(Job job, CancellationToken ct, IReadOnlyList<string> items)
    {
        string first = items[0].TrimEnd('\\', '/');
        string baseName = items.Count == 1 ? Path.GetFileNameWithoutExtension(first) : "Archive";
        if (items.Count == 1 && Directory.Exists(first)) baseName = Path.GetFileName(first);
        var outp = OutputPaths.ReserveIn(OutputPaths.DirFor(first), baseName, "zip");
        try
        {
            var entries = new List<(string full, string name)>();
            foreach (var raw in items)
            {
                var item = raw.TrimEnd('\\', '/');
                if (Directory.Exists(item))
                {
                    var root = Path.GetDirectoryName(item) ?? item;
                    foreach (var f in Directory.EnumerateFiles(item, "*", SearchOption.AllDirectories))
                        entries.Add((f, Path.GetRelativePath(root, f)));
                }
                else entries.Add((item, Path.GetFileName(item)));
            }
            entries.RemoveAll(e => string.Equals(Path.GetFullPath(e.full), Path.GetFullPath(outp), StringComparison.OrdinalIgnoreCase));

            using (var fs = new FileStream(outp, FileMode.Create))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                int i = 0;
                foreach (var (full, name) in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    zip.CreateEntryFromFile(full, name.Replace('\\', '/'), CompressionLevel.Optimal);
                    job.Report(++i / (double)Math.Max(1, entries.Count), L($"{i}/{entries.Count} fichiers", $"{i}/{entries.Count} files"));
                }
            }
            job.AddOutput(outp);
            job.SetStatus($"{entries.Count} {L("fichier", "file")}{(entries.Count > 1 ? "s" : "")} · {FileKinds.HumanSize(new FileInfo(outp).Length)}");
        }
        catch { OutputPaths.TryDelete(outp); throw; }
    }
}
