using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Lobster;

public enum FileKind { Image, Video, Audio, Pdf, Document, Spreadsheet, Presentation, Markdown, Folder, Other }

public static class FileKinds
{
    private static readonly Dictionary<string, FileKind> Map = new(StringComparer.OrdinalIgnoreCase);

    static FileKinds()
    {
        void Add(FileKind k, string exts) { foreach (var e in exts.Split(' ')) Map[e] = k; }
        Add(FileKind.Image, "png jpg jpeg jfif jpe webp avif heic heif gif bmp tif tiff ico svg psd tga dds exr jxl cr2 cr3 nef arw dng raf orf rw2 pef srw");
        Add(FileKind.Video, "mp4 m4v mov mkv webm avi wmv flv mpg mpeg ts mts m2ts 3gp 3g2 ogv vob");
        Add(FileKind.Audio, "mp3 m4a aac wav flac ogg oga opus wma aiff aif ape amr m4b caf");
        Add(FileKind.Pdf, "pdf");
        Add(FileKind.Document, "doc docx docm odt rtf txt html htm");
        Add(FileKind.Spreadsheet, "xls xlsx xlsm ods csv");
        Add(FileKind.Presentation, "ppt pptx odp");
        Add(FileKind.Markdown, "md markdown");
    }

    public static string Ext(string path) => Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

    public static FileKind Of(string path)
    {
        if (Directory.Exists(path)) return FileKind.Folder;
        return Map.TryGetValue(Ext(path), out var k) ? k : FileKind.Other;
    }

    public static bool IsRasterCamera(string path) =>
        Ext(path) is "cr2" or "cr3" or "nef" or "arw" or "dng" or "raf" or "orf" or "rw2" or "pef" or "srw";

    public static FileKind? Common(IReadOnlyList<string> files)
    {
        var kinds = files.Select(Of).Distinct().ToList();
        return kinds.Count == 1 ? kinds[0] : null;
    }

    public static bool CanBecomePdf(FileKind k) =>
        k is FileKind.Image or FileKind.Pdf or FileKind.Document or FileKind.Spreadsheet or FileKind.Presentation or FileKind.Markdown;

    public static string Glyph(FileKind? k) => k switch
    {
        FileKind.Image => "",
        FileKind.Video => "",
        FileKind.Audio => "",
        FileKind.Pdf or FileKind.Document or FileKind.Markdown => "",
        FileKind.Spreadsheet => "",
        FileKind.Presentation => "",
        FileKind.Folder => "",
        _ => ""
    };

    public static string Describe(IReadOnlyList<string> files)
    {
        int n = files.Count;
        var k = Common(files);
        string Pl(string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";
        return k switch
        {
            FileKind.Image => Pl("image", "images"),
            FileKind.Video => Pl(Loc.L("vidéo", "video"), Loc.L("vidéos", "videos")),
            FileKind.Audio => Pl(Loc.L("audio", "audio file"), Loc.L("audios", "audio files")),
            FileKind.Pdf => Pl("PDF", Loc.L("PDF", "PDFs")),
            FileKind.Document => Pl("document", "documents"),
            FileKind.Spreadsheet => Pl(Loc.L("classeur", "spreadsheet"), Loc.L("classeurs", "spreadsheets")),
            FileKind.Presentation => Pl(Loc.L("présentation", "presentation"), Loc.L("présentations", "presentations")),
            FileKind.Markdown => Pl("Markdown", "Markdown"),
            FileKind.Folder => Pl(Loc.L("dossier", "folder"), Loc.L("dossiers", "folders")),
            _ => Pl(Loc.L("fichier", "file"), Loc.L("fichiers", "files"))
        };
    }

    public static string HumanSize(long bytes)
    {
        string[] u = Loc.SizeUnits;
        double v = bytes; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{v:0} {u[i]}" : $"{v:0.#} {u[i]}";
    }
}
