using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using static Lobster.Loc;

namespace Lobster;

/// <summary>
/// Office documents: uses Microsoft Office (automation) when installed,
/// otherwise LibreOffice in headless mode. Both run locally.
/// </summary>
public static class OfficeEngine
{
    private static readonly SemaphoreSlim OfficeGate = new(1);

    public static string MissingMessage => L(
        "Pour convertir des documents Office, installez Microsoft Office ou LibreOffice (gratuit).",
        "To convert Office documents, install Microsoft Office or LibreOffice (free).");

    public static bool Available(FileKind kind) => kind switch
    {
        FileKind.Spreadsheet => Tools.HasExcel || Tools.LibreOffice != null,
        FileKind.Presentation => Tools.HasPowerPoint || Tools.LibreOffice != null,
        _ => Tools.HasWord || Tools.LibreOffice != null,
    };

    /// <summary>Convert src into outPath (already reserved) using the best available engine.</summary>
    public static async Task Convert(string src, string targetExt, string outPath, CancellationToken ct)
    {
        var srcExt = FileKinds.Ext(src);
        var kind = srcExt is "html" or "htm" ? FileKind.Document : FileKinds.Of(src);
        bool useMs = kind switch
        {
            FileKind.Spreadsheet => Tools.HasExcel,
            FileKind.Presentation => Tools.HasPowerPoint,
            FileKind.Pdf => Tools.HasWord,
            _ => Tools.HasWord,
        };

        if (useMs)
        {
            await OfficeGate.WaitAsync(ct);
            try
            {
                await RunSta(() =>
                {
                    switch (kind)
                    {
                        case FileKind.Spreadsheet: Excel(src, targetExt, outPath); break;
                        case FileKind.Presentation: PowerPoint(src, targetExt, outPath); break;
                        default: Word(src, targetExt, outPath); break;
                    }
                });
            }
            finally { OfficeGate.Release(); }
            return;
        }

        if (kind == FileKind.Pdf)
            throw new ToolMissingException("word", L("La conversion PDF → Word nécessite Microsoft Word.", "PDF → Word conversion requires Microsoft Word."));

        var lo = Tools.LibreOffice ?? throw new ToolMissingException("office", MissingMessage);
        await LibreOffice(lo, src, targetExt, outPath, ct);
    }

    // ---------------------------------------------------------------- Microsoft Office

    private static Task RunSta(Action action)
    {
        var tcs = new TaskCompletionSource();
        var t = new Thread(() =>
        {
            try { action(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        }) { IsBackground = true, Name = "Office automation" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return tcs.Task;
    }

    private static dynamic Create(string progId) =>
        Activator.CreateInstance(Type.GetTypeFromProgID(progId) ?? throw new Exception($"{progId} indisponible"))!;

    private static void Release(object? o)
    {
        if (o != null && Marshal.IsComObject(o)) { try { Marshal.FinalReleaseComObject(o); } catch { } }
    }

    private static void Word(string src, string ext, string outp)
    {
        dynamic? app = null, doc = null;
        try
        {
            app = Create("Word.Application");
            app.Visible = false;
            app.DisplayAlerts = 0; // wdAlertsNone
            // Open(FileName, ConfirmConversions, ReadOnly, AddToRecentFiles)
            doc = app.Documents.Open(Path.GetFullPath(src), false, true, false);
            int fmt = ext switch
            {
                "pdf" => 17,
                "docx" => 16,
                "doc" => 0,
                "odt" => 23,
                "rtf" => 6,
                "html" => 10,
                "txt" => 2,
                _ => throw new Exception(L($"Format {ext} non pris en charge par Word", $"Format {ext} is not supported by Word"))
            };
            if (ext == "txt")
                // SaveAs2(FileName, FileFormat, LockComments, Password, AddToRecentFiles, WritePassword,
                //         ReadOnlyRecommended, EmbedTrueTypeFonts, SaveNativePictureFormat, SaveFormsData,
                //         SaveAsAOCELetter, Encoding)
                doc.SaveAs2(outp, fmt, false, "", false, "", false, false, false, false, false, 65001);
            else
                doc.SaveAs2(outp, fmt);
        }
        finally
        {
            try { doc?.Close(false); } catch { }
            try { app?.Quit(false); } catch { }
            Release(doc); Release(app);
        }
    }

    private static void Excel(string src, string ext, string outp)
    {
        dynamic? app = null, wb = null;
        try
        {
            app = Create("Excel.Application");
            app.Visible = false;
            app.DisplayAlerts = false;
            // Open(Filename, UpdateLinks, ReadOnly)
            wb = app.Workbooks.Open(Path.GetFullPath(src), 0, true);
            if (ext == "pdf")
            {
                wb.ExportAsFixedFormat(0, outp); // xlTypePDF
            }
            else
            {
                int fmt = ext switch
                {
                    "xlsx" => 51,
                    "xls" => 56,
                    "ods" => 60,
                    "csv" => 62, // xlCSVUTF8
                    _ => throw new Exception(L($"Format {ext} non pris en charge par Excel", $"Format {ext} is not supported by Excel"))
                };
                try { wb.SaveAs(outp, fmt); }
                catch when (fmt == 62) { wb.SaveAs(outp, 6); }
            }
        }
        finally
        {
            try { wb?.Close(false); } catch { }
            try { app?.Quit(); } catch { }
            Release(wb); Release(app);
        }
    }

    private static void PowerPoint(string src, string ext, string outp)
    {
        dynamic? app = null, pres = null;
        try
        {
            app = Create("PowerPoint.Application");
            // Open(FileName, ReadOnly, Untitled, WithWindow) — msoTrue = -1, msoFalse = 0
            pres = app.Presentations.Open(Path.GetFullPath(src), -1, 0, 0);
            int fmt = ext switch
            {
                "pdf" => 32,
                "pptx" => 24,
                "ppt" => 1,
                "odp" => 35,
                _ => throw new Exception(L($"Format {ext} non pris en charge par PowerPoint", $"Format {ext} is not supported by PowerPoint"))
            };
            pres.SaveAs(outp, fmt);
        }
        finally
        {
            try { pres?.Close(); } catch { }
            try { app?.Quit(); } catch { }
            Release(pres); Release(app);
        }
    }

    // ---------------------------------------------------------------- LibreOffice

    private static async Task LibreOffice(string soffice, string src, string ext, string outp, CancellationToken ct)
    {
        var tmp = OutputPaths.TempDir();
        var profile = Path.Combine(Settings.AppDataDir, "lo-profile");
        var filter = ext switch
        {
            "txt" => "txt:Text (encoded):UTF8",
            "csv" => "csv:Text - txt - csv (StarCalc):44,34,76",
            "html" => "html",
            _ => ext
        };
        var args = new System.Collections.Generic.List<string>
        {
            $"-env:UserInstallation={new Uri(profile).AbsoluteUri}",
            "--headless", "--norestore", "--nolockcheck"
        };
        if (FileKinds.Ext(src) is "html" or "htm") args.Add("--infilter=HTML (StarWriter)");
        args.AddRange(new[] { "--convert-to", filter, "--outdir", tmp, Path.GetFullPath(src) });

        var r = await ProcessRunner.Run(soffice, args, ct);
        var produced = Directory.GetFiles(tmp).FirstOrDefault();
        if (produced == null)
            throw new Exception(L("LibreOffice n'a pas pu convertir le fichier. ", "LibreOffice could not convert the file. ") + ProcessRunner.Summarize(r.StdErrTail + "\n" + r.StdOut));
        File.Move(produced, outp, overwrite: true);
        OutputPaths.TryDelete(tmp);
    }
}
