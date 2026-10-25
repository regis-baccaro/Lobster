using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using static Lobster.Loc;

namespace Lobster;

public sealed class MenuNode
{
    public string Label { get; }
    public string? Sub { get; init; }
    public string? Glyph { get; init; }
    public string? Hint { get; init; }
    public List<MenuNode> Children { get; init; } = new();
    public Action<IReadOnlyList<string>>? Invoke { get; init; }
    public bool IsLeaf => Invoke != null;

    public MenuNode(string label) => Label = label;
}

/// <summary>Builds the radial menu for a set of files, and wires every action to an engine.</summary>
public static class Catalog
{
    private static class G
    {
        public const string Convert = "", Compress = "", Resize = "", Crop = "",
            Rotate = "", Cut = "", Merge = "", Archive = "", Effects = "",
            Audio = "", Pages = "", Speed = "", Copy = "", Pdf = "", Image = "";
    }

    public static List<MenuNode> Build(IReadOnlyList<string> files)
    {
        var kind = FileKinds.Common(files);
        var menu = kind switch
        {
            FileKind.Image => ImageMenu(files),
            FileKind.Video => VideoMenu(files),
            FileKind.Audio => AudioMenu(files),
            FileKind.Pdf => PdfMenu(files),
            FileKind.Document => OfficeMenu(files, FileKind.Document, new[] { "pdf", "docx", "odt", "rtf", "txt", "html" }),
            FileKind.Spreadsheet => OfficeMenu(files, FileKind.Spreadsheet, new[] { "pdf", "xlsx", "ods", "csv" }),
            FileKind.Presentation => PresentationMenu(files),
            FileKind.Markdown => MarkdownMenu(files),
            _ => MixedMenu(files),
        };
        menu.Add(ArchiveMenu());
        return menu.Where(c => c.IsLeaf || c.Children.Count > 0).ToList();
    }

    // ================================================================ helpers

    private static MenuNode Cat(string label, string glyph, params MenuNode?[] kids) =>
        new(label) { Glyph = glyph, Children = kids.Where(k => k != null).Select(k => k!).ToList() };

    private static MenuNode Leaf(string label, string? sub, string hint, Action<IReadOnlyList<string>> act, string? glyph = null) =>
        new(label) { Sub = sub, Hint = hint, Invoke = act, Glyph = glyph };

    private static string Name(string f) => Path.GetFileName(f.TrimEnd('\\', '/'));

    private static void Each(IReadOnlyList<string> files, string verb, Func<Job, CancellationToken, string, Task> work)
    {
        foreach (var f in files) AppState.Jobs.Run($"{verb} · {Name(f)}", (j, ct) => work(j, ct, f));
    }

    private static void EachSync(IReadOnlyList<string> files, string verb, Action<Job, CancellationToken, string> work) =>
        Each(files, verb, (j, ct, f) => { work(j, ct, f); return Task.CompletedTask; });

    private static Action<IReadOnlyList<string>> Ff(Action<IReadOnlyList<string>> a) =>
        files => Requirements.WithFfmpeg(() => a(files));

    private static Action<IReadOnlyList<string>> Office(FileKind kind, Action<IReadOnlyList<string>> a) =>
        files => Requirements.WithOffice(kind, () => a(files));

    private static string? SharedExt(IReadOnlyList<string> files)
    {
        var exts = files.Select(FileKinds.Ext).Select(e => e == "jpeg" ? "jpg" : e == "tif" ? "tiff" : e).Distinct().ToList();
        return exts.Count == 1 ? exts[0] : null;
    }

    private static string Up(string ext) => ext.ToUpperInvariant();

    // ================================================================ images

    private static List<MenuNode> ImageMenu(IReadOnlyList<string> files)
    {
        var shared = SharedExt(files);
        bool allGif = shared == "gif";
        var converts = ImageEngine.Targets.Where(t => t != shared)
            .Select(t => Leaf(Up(t), t == "ico" ? L("icône", "icon") : t == "pdf" ? L("1 page/image", "1 page/image") : null, L($"Convertir en {Up(t)}", $"Convert to {Up(t)}"),
                fs => EachSync(fs, $"→ {Up(t)}", (j, ct, f) => ImageEngine.Convert(j, ct, f, t))))
            .ToList();
        if (allGif)
        {
            converts.Add(Leaf("MP4", L("vidéo", "video"), L("GIF → vidéo MP4", "GIF → MP4 video"), Ff(fs => Each(fs, "→ MP4", (j, ct, f) => FfmpegEngine.GifToVideo(j, ct, f, "mp4")))));
            converts.Add(Leaf("WEBM", L("vidéo", "video"), L("GIF → vidéo WEBM", "GIF → WEBM video"), Ff(fs => Each(fs, "→ WEBM", (j, ct, f) => FfmpegEngine.GifToVideo(j, ct, f, "webm")))));
        }

        var menu = new List<MenuNode>
        {
            Cat(L("Convertir", "Convert"), G.Convert, converts.ToArray()),
            Cat(L("Compresser", "Compress"), G.Compress,
                Leaf(L("Léger", "Light"), L("qualité 82", "quality 82"), L("Compresser légèrement", "Compress lightly"), fs => EachSync(fs, L("Compresser", "Compress"), (j, ct, f) => ImageEngine.Compress(j, ct, f, 1))),
                Leaf(L("Moyen", "Medium"), L("qualité 68", "quality 68"), L("Compresser (équilibré)", "Compress (balanced)"), fs => EachSync(fs, L("Compresser", "Compress"), (j, ct, f) => ImageEngine.Compress(j, ct, f, 2))),
                Leaf(L("Fort", "Strong"), "≤ 2560 px", L("Compresser fortement", "Compress strongly"), fs => EachSync(fs, L("Compresser", "Compress"), (j, ct, f) => ImageEngine.Compress(j, ct, f, 3))),
                Leaf("Web", "WebP 1920", L("Optimiser pour le web (WebP, 1920 px)", "Optimize for the web (WebP, 1920 px)"), fs => EachSync(fs, "Web", (j, ct, f) => ImageEngine.ForWeb(j, ct, f)))),
            Cat(L("Taille", "Size"), G.Resize,
                Leaf("25 %", null, L("Réduire à 25 %", "Scale to 25%"), fs => EachSync(fs, "25 %", (j, ct, f) => ImageEngine.ResizePercent(j, ct, f, 25))),
                Leaf("50 %", null, L("Réduire à 50 %", "Scale to 50%"), fs => EachSync(fs, "50 %", (j, ct, f) => ImageEngine.ResizePercent(j, ct, f, 50))),
                Leaf("75 %", null, L("Réduire à 75 %", "Scale to 75%"), fs => EachSync(fs, "75 %", (j, ct, f) => ImageEngine.ResizePercent(j, ct, f, 75))),
                Leaf("3840", "px max", L("Côté le plus long ≤ 3840 px", "Longest side ≤ 3840 px"), fs => EachSync(fs, "3840 px", (j, ct, f) => ImageEngine.ResizeMax(j, ct, f, 3840))),
                Leaf("1920", "px max", L("Côté le plus long ≤ 1920 px", "Longest side ≤ 1920 px"), fs => EachSync(fs, "1920 px", (j, ct, f) => ImageEngine.ResizeMax(j, ct, f, 1920))),
                Leaf("1280", "px max", L("Côté le plus long ≤ 1280 px", "Longest side ≤ 1280 px"), fs => EachSync(fs, "1280 px", (j, ct, f) => ImageEngine.ResizeMax(j, ct, f, 1280))),
                Leaf("800", "px max", L("Côté le plus long ≤ 800 px", "Longest side ≤ 800 px"), fs => EachSync(fs, "800 px", (j, ct, f) => ImageEngine.ResizeMax(j, ct, f, 800))),
                Leaf(L("Autre…", "Other…"), null, L("Choisir une taille", "Choose a size"), async fs =>
                {
                    var v = await Dialogs.AskNumber(L("Redimensionner", "Resize"), L("Côté le plus long (en pixels) :", "Longest side (pixels):"), "1600", 16, 20000);
                    if (v is int px) EachSync(fs, $"{px} px", (j, ct, f) => ImageEngine.ResizeMax(j, ct, f, (uint)px));
                })),
            Leaf(L("Rogner", "Crop"), null, L("Rogner…", "Crop…"), CropImages, G.Crop),
            Cat(L("Pivoter", "Rotate"), G.Rotate,
                Leaf("90° ↻", null, L("Pivoter de 90° à droite", "Rotate 90° clockwise"), fs => EachSync(fs, L("Pivoter", "Rotate"), (j, ct, f) => ImageEngine.Edit(j, ct, f, L("pivoté", "rotated"), i => i.Rotate(90)))),
                Leaf("90° ↺", null, L("Pivoter de 90° à gauche", "Rotate 90° counter-clockwise"), fs => EachSync(fs, L("Pivoter", "Rotate"), (j, ct, f) => ImageEngine.Edit(j, ct, f, L("pivoté", "rotated"), i => i.Rotate(-90)))),
                Leaf("180°", null, L("Pivoter de 180°", "Rotate 180°"), fs => EachSync(fs, L("Pivoter", "Rotate"), (j, ct, f) => ImageEngine.Edit(j, ct, f, L("pivoté", "rotated"), i => i.Rotate(180)))),
                Leaf(L("Miroir", "Mirror"), "horizontal", L("Retourner horizontalement", "Flip horizontally"), fs => EachSync(fs, L("Miroir", "Mirror"), (j, ct, f) => ImageEngine.Edit(j, ct, f, L("miroir", "mirrored"), i => i.Flop()))),
                Leaf(L("Miroir", "Mirror"), "vertical", L("Retourner verticalement", "Flip vertically"), fs => EachSync(fs, L("Miroir", "Mirror"), (j, ct, f) => ImageEngine.Edit(j, ct, f, L("miroir", "mirrored"), i => i.Flip())))),
            Cat(L("Retouches", "Touch-ups"), G.Effects,
                Leaf(L("N & B", "B & W"), L("niveaux de gris", "grayscale"), L("Convertir en noir et blanc", "Convert to black & white"), fs => EachSync(fs, L("N & B", "B & W"), (j, ct, f) => ImageEngine.Edit(j, ct, f, L("N&B", "B&W"), i => i.Grayscale()))),
                Leaf("EXIF", L("supprimer", "remove"), L("Supprimer les métadonnées (GPS, appareil…)", "Remove metadata (GPS, camera…)"), fs => EachSync(fs, L("Métadonnées", "Metadata"), (j, ct, f) => ImageEngine.Edit(j, ct, f, L("sans EXIF", "no EXIF"), i => i.Strip())))),
        };

        if (files.Count >= 2)
            menu.Add(Cat(L("Fusionner", "Merge"), G.Merge,
                Leaf("PDF", L("1 page/image", "1 page/image"), L("Assembler en un PDF", "Combine into one PDF"), fs => AppState.Jobs.Run($"PDF · {fs.Count} images", (j, ct) => Pipelines.MergeToPdf(j, ct, fs))),
                Leaf("GIF", L("animé", "animated"), L("Créer un GIF animé", "Create an animated GIF"), fs => AppState.Jobs.Run($"GIF · {fs.Count} images", (j, ct) => { ImageEngine.MergeGif(j, ct, fs); return Task.CompletedTask; })),
                Leaf(L("Côte à côte", "Side by side"), null, L("Assembler horizontalement", "Join horizontally"), fs => AppState.Jobs.Run(L($"Assembler · {fs.Count} images", $"Join · {fs.Count} images"), (j, ct) => { ImageEngine.Append(j, ct, fs, true); return Task.CompletedTask; })),
                Leaf(L("Empilées", "Stacked"), null, L("Assembler verticalement", "Join vertically"), fs => AppState.Jobs.Run(L($"Assembler · {fs.Count} images", $"Join · {fs.Count} images"), (j, ct) => { ImageEngine.Append(j, ct, fs, false); return Task.CompletedTask; }))));

        return menu;
    }

    private static async void CropImages(IReadOnlyList<string> files)
    {
        (string path, int width, int height) preview;
        try { preview = await Task.Run(() => ImageEngine.Preview(files[0])); }
        catch (Exception ex) { await Dialogs.Error(L("Rogner", "Crop"), ex.Message); return; }
        var sel = await AppState.Host.Crop(preview.path, preview.width, preview.height, files.Count > 1 ? L($"Le même cadrage sera appliqué aux {files.Count} images.", $"The same crop will be applied to all {files.Count} images.") : null);
        if (sel is { } r)
            EachSync(files, L("Rogner", "Crop"), (j, ct, f) => ImageEngine.Crop(j, ct, f, r.X, r.Y, r.Width, r.Height));
    }

    // ================================================================ video

    private static List<MenuNode> VideoMenu(IReadOnlyList<string> files)
    {
        var shared = SharedExt(files);
        var targets = new (string id, string label, string? sub)[]
        {
            ("mp4", "MP4", "H.264"), ("hevc", "MP4", "H.265"), ("webm", "WEBM", "VP9"), ("mkv", "MKV", null),
            ("mov", "MOV", null), ("avi", "AVI", null), ("gif", "GIF", L("animé", "animated"))
        };
        var menu = new List<MenuNode>
        {
            Cat(L("Convertir", "Convert"), G.Convert, targets.Where(t => t.id != shared)
                .Select(t => Leaf(t.label, t.sub, L("Convertir en ", "Convert to ") + t.label + (t.sub != null ? " " + t.sub : ""),
                    Ff(fs => Each(fs, $"→ {t.label}", (j, ct, f) => FfmpegEngine.ConvertVideo(j, ct, f, t.id))))).ToArray()),
            Cat(L("Compresser", "Compress"), G.Compress,
                Leaf(L("Léger", "Light"), "CRF 24", L("Compresser en gardant la qualité", "Compress, keep quality"), Ff(fs => Each(fs, L("Compresser", "Compress"), (j, ct, f) => FfmpegEngine.CompressVideo(j, ct, f, 24, 0, "160k")))),
                Leaf(L("Moyen", "Medium"), "CRF 28", L("Compresser (équilibré)", "Compress (balanced)"), Ff(fs => Each(fs, L("Compresser", "Compress"), (j, ct, f) => FfmpegEngine.CompressVideo(j, ct, f, 28, 1080, "128k")))),
                Leaf(L("Fort", "Strong"), "720p", L("Compresser fortement (720p)", "Compress strongly (720p)"), Ff(fs => Each(fs, L("Compresser", "Compress"), (j, ct, f) => FfmpegEngine.CompressVideo(j, ct, f, 31, 720, "96k")))),
                Leaf(L("10 Mo", "10 MB"), "max", L("Viser 10 Mo (messageries)", "Target 10 MB (messaging)"), Ff(fs => Each(fs, L("≤ 10 Mo", "≤ 10 MB"), (j, ct, f) => FfmpegEngine.CompressVideoToSize(j, ct, f, 10)))),
                Leaf(L("25 Mo", "25 MB"), "max", L("Viser 25 Mo (e-mail)", "Target 25 MB (email)"), Ff(fs => Each(fs, L("≤ 25 Mo", "≤ 25 MB"), (j, ct, f) => FfmpegEngine.CompressVideoToSize(j, ct, f, 25)))),
                Leaf(L("50 Mo", "50 MB"), "max", L("Viser 50 Mo", "Target 50 MB"), Ff(fs => Each(fs, L("≤ 50 Mo", "≤ 50 MB"), (j, ct, f) => FfmpegEngine.CompressVideoToSize(j, ct, f, 50))))),
            Cat(L("Taille", "Size"), G.Resize,
                Leaf("2160p", "4K", L("Redimensionner en 2160p", "Resize to 2160p"), Ff(fs => Each(fs, "2160p", (j, ct, f) => FfmpegEngine.ResizeVideo(j, ct, f, 2160)))),
                Leaf("1080p", null, L("Redimensionner en 1080p", "Resize to 1080p"), Ff(fs => Each(fs, "1080p", (j, ct, f) => FfmpegEngine.ResizeVideo(j, ct, f, 1080)))),
                Leaf("720p", null, L("Redimensionner en 720p", "Resize to 720p"), Ff(fs => Each(fs, "720p", (j, ct, f) => FfmpegEngine.ResizeVideo(j, ct, f, 720)))),
                Leaf("480p", null, L("Redimensionner en 480p", "Resize to 480p"), Ff(fs => Each(fs, "480p", (j, ct, f) => FfmpegEngine.ResizeVideo(j, ct, f, 480)))),
                Leaf("50 %", null, L("Réduire de moitié", "Halve the size"), Ff(fs => Each(fs, "50 %", (j, ct, f) => FfmpegEngine.ResizeVideoPercent(j, ct, f, 50))))),
            Leaf(L("Rogner", "Crop"), null, L("Rogner l'image…", "Crop the frame…"), Ff(CropVideos), G.Crop),
            Leaf(L("Découper", "Trim"), null, L("Garder un extrait…", "Keep a clip…"), Ff(fs => TrimMedia(fs, true)), G.Cut),
            Cat(L("Pivoter", "Rotate"), G.Rotate,
                Leaf("90° ↻", null, L("Pivoter de 90° à droite", "Rotate 90° clockwise"), Ff(fs => Each(fs, L("Pivoter", "Rotate"), (j, ct, f) => FfmpegEngine.RotateVideo(j, ct, f, "transpose=1", L("pivoté", "rotated"))))),
                Leaf("90° ↺", null, L("Pivoter de 90° à gauche", "Rotate 90° counter-clockwise"), Ff(fs => Each(fs, L("Pivoter", "Rotate"), (j, ct, f) => FfmpegEngine.RotateVideo(j, ct, f, "transpose=2", L("pivoté", "rotated"))))),
                Leaf("180°", null, L("Pivoter de 180°", "Rotate 180°"), Ff(fs => Each(fs, L("Pivoter", "Rotate"), (j, ct, f) => FfmpegEngine.RotateVideo(j, ct, f, "hflip,vflip", L("pivoté", "rotated"))))),
                Leaf(L("Miroir", "Mirror"), null, L("Retourner horizontalement", "Flip horizontally"), Ff(fs => Each(fs, L("Miroir", "Mirror"), (j, ct, f) => FfmpegEngine.RotateVideo(j, ct, f, "hflip", L("miroir", "mirrored")))))),
            Cat("Audio", G.Audio,
                Leaf("MP3", L("extraire", "extract"), L("Extraire l'audio en MP3", "Extract audio as MP3"), Ff(fs => Each(fs, "→ MP3", (j, ct, f) => FfmpegEngine.ExtractAudio(j, ct, f, "mp3")))),
                Leaf("M4A", L("extraire", "extract"), L("Extraire l'audio en M4A (AAC)", "Extract audio as M4A (AAC)"), Ff(fs => Each(fs, "→ M4A", (j, ct, f) => FfmpegEngine.ExtractAudio(j, ct, f, "m4a")))),
                Leaf("WAV", L("extraire", "extract"), L("Extraire l'audio en WAV", "Extract audio as WAV"), Ff(fs => Each(fs, "→ WAV", (j, ct, f) => FfmpegEngine.ExtractAudio(j, ct, f, "wav")))),
                Leaf("FLAC", L("extraire", "extract"), L("Extraire l'audio en FLAC", "Extract audio as FLAC"), Ff(fs => Each(fs, "→ FLAC", (j, ct, f) => FfmpegEngine.ExtractAudio(j, ct, f, "flac")))),
                Leaf(L("Muet", "Mute"), L("sans son", "no sound"), L("Supprimer la piste audio", "Remove the audio track"), Ff(fs => Each(fs, L("Sans son", "Mute"), (j, ct, f) => FfmpegEngine.Mute(j, ct, f)))),
                Leaf(L("Normaliser", "Normalize"), "volume", L("Normaliser le volume", "Normalize loudness"), Ff(fs => Each(fs, L("Normaliser", "Normalize"), (j, ct, f) => FfmpegEngine.AudioFilter(j, ct, f, true, L("normalisé", "normalized"), _ => "loudnorm=I=-16:TP=-1.5:LRA=11"))))),
            Cat(L("Effets", "Effects"), G.Speed,
                Leaf("×2", L("vitesse", "speed"), L("Accélérer ×2", "Speed up ×2"), Ff(fs => Each(fs, "×2", (j, ct, f) => FfmpegEngine.Speed(j, ct, f, 2, true)))),
                Leaf(L("×1,5", "×1.5"), L("vitesse", "speed"), L("Accélérer ×1,5", "Speed up ×1.5"), Ff(fs => Each(fs, L("×1,5", "×1.5"), (j, ct, f) => FfmpegEngine.Speed(j, ct, f, 1.5, true)))),
                Leaf(L("×0,5", "×0.5"), L("ralenti", "slow-mo"), L("Ralentir ×0,5", "Slow down ×0.5"), Ff(fs => Each(fs, L("×0,5", "×0.5"), (j, ct, f) => FfmpegEngine.Speed(j, ct, f, 0.5, true)))),
                Leaf("Capture", "PNG", L("Capturer l'image du milieu", "Grab the middle frame"), Ff(fs => Each(fs, "Capture", (j, ct, f) => FfmpegEngine.Capture(j, ct, f))))),
        };
        if (files.Count >= 2)
            menu.Add(Leaf(L("Fusionner", "Merge"), null, L("Mettre bout à bout (MP4)", "Join end to end (MP4)"),
                Ff(fs => AppState.Jobs.Run(L($"Fusion · {fs.Count} vidéos", $"Merge · {fs.Count} videos"), (j, ct) => FfmpegEngine.ConcatVideos(j, ct, fs))), G.Merge));
        return menu;
    }

    private static async void CropVideos(IReadOnlyList<string> files)
    {
        try
        {
            var info = await FfmpegEngine.Probe(files[0], CancellationToken.None);
            var frame = await FfmpegEngine.ExtractFrame(files[0], info.Duration > 2 ? info.Duration * 0.2 : 0, CancellationToken.None);
            var sel = await AppState.Host.Crop(frame, info.Width, info.Height, files.Count > 1 ? L($"Le même cadrage sera appliqué aux {files.Count} vidéos.", $"The same crop will be applied to all {files.Count} videos.") : null);
            if (sel is { } r)
                Each(files, L("Rogner", "Crop"), (j, ct, f) => FfmpegEngine.CropVideo(j, ct, f, r.X, r.Y, r.Width, r.Height));
        }
        catch (Exception ex) { await Dialogs.Error(L("Rogner", "Crop"), ex.Message); }
    }

    private static async void TrimMedia(IReadOnlyList<string> files, bool isVideo)
    {
        foreach (var f in files)
        {
            try
            {
                var info = await FfmpegEngine.Probe(f, CancellationToken.None);
                if (info.Duration <= 0) throw new Exception(L("Durée inconnue pour ce fichier.", "Unknown duration for this file."));
                var range = await AppState.Host.Trim(f, info.Duration, isVideo && info.HasVideo);
                if (range is not { } t) return;
                double s = t.Start, e = t.End; bool fast = t.Fast;
                AppState.Jobs.Run(L($"Découper · {Name(f)}", $"Trim · {Name(f)}"), (j, ct) => FfmpegEngine.Trim(j, ct, f, s, e, fast, isVideo));
            }
            catch (Exception ex) { await Dialogs.Error(L("Découper", "Trim"), ex.Message); }
        }
    }

    // ================================================================ audio

    private static List<MenuNode> AudioMenu(IReadOnlyList<string> files)
    {
        var shared = SharedExt(files);
        var targets = new[] { "mp3", "m4a", "wav", "flac", "ogg", "opus", "aiff", "wma" };
        var menu = new List<MenuNode>
        {
            Cat(L("Convertir", "Convert"), G.Convert, targets.Where(t => t != shared)
                .Select(t => Leaf(Up(t), t is "wav" or "flac" or "aiff" ? L("sans perte", "lossless") : null, L($"Convertir en {Up(t)}", $"Convert to {Up(t)}"),
                    Ff(fs => Each(fs, $"→ {Up(t)}", (j, ct, f) => FfmpegEngine.ConvertAudio(j, ct, f, t))))).ToArray()),
            Cat(L("Compresser", "Compress"), G.Compress,
                Leaf("192", "kbps", L("Compresser à 192 kbps", "Compress to 192 kbps"), Ff(fs => Each(fs, "192 kbps", (j, ct, f) => FfmpegEngine.CompressAudio(j, ct, f, "192k")))),
                Leaf("128", "kbps", L("Compresser à 128 kbps", "Compress to 128 kbps"), Ff(fs => Each(fs, "128 kbps", (j, ct, f) => FfmpegEngine.CompressAudio(j, ct, f, "128k")))),
                Leaf("96", "kbps", L("Compresser à 96 kbps", "Compress to 96 kbps"), Ff(fs => Each(fs, "96 kbps", (j, ct, f) => FfmpegEngine.CompressAudio(j, ct, f, "96k")))),
                Leaf("64", "kbps", L("Compresser à 64 kbps (voix)", "Compress to 64 kbps (voice)"), Ff(fs => Each(fs, "64 kbps", (j, ct, f) => FfmpegEngine.CompressAudio(j, ct, f, "64k"))))),
            Leaf(L("Découper", "Trim"), null, L("Garder un extrait…", "Keep a clip…"), Ff(fs => TrimMedia(fs, false)), G.Cut),
            Cat(L("Effets", "Effects"), G.Effects,
                Leaf(L("Normaliser", "Normalize"), "volume", L("Normaliser le volume (−16 LUFS)", "Normalize loudness (−16 LUFS)"), Ff(fs => Each(fs, L("Normaliser", "Normalize"), (j, ct, f) => FfmpegEngine.AudioFilter(j, ct, f, false, L("normalisé", "normalized"), _ => "loudnorm=I=-16:TP=-1.5:LRA=11")))),
                Leaf("Mono", null, L("Convertir en mono", "Convert to mono"), Ff(fs => Each(fs, "Mono", (j, ct, f) => FfmpegEngine.AudioFilter(j, ct, f, false, "mono", _ => "anull", mono: true)))),
                Leaf(L("Fondu", "Fade"), "2 s", L("Fondu en entrée et en sortie", "Fade in and out"), Ff(fs => Each(fs, L("Fondu", "Fade"), (j, ct, f) => FfmpegEngine.AudioFilter(j, ct, f, false, L("fondu", "faded"),
                    i => $"afade=t=in:d=2,afade=t=out:st={FfmpegEngine.F(Math.Max(0, i.Duration - 2))}:d=2")))),
                Leaf(L("×1,5", "×1.5"), L("vitesse", "speed"), L("Accélérer ×1,5", "Speed up ×1.5"), Ff(fs => Each(fs, L("×1,5", "×1.5"), (j, ct, f) => FfmpegEngine.Speed(j, ct, f, 1.5, false)))),
                Leaf(L("×0,75", "×0.75"), L("vitesse", "speed"), L("Ralentir ×0,75", "Slow down ×0.75"), Ff(fs => Each(fs, L("×0,75", "×0.75"), (j, ct, f) => FfmpegEngine.Speed(j, ct, f, 0.75, false))))),
        };
        if (files.Count >= 2)
            menu.Add(Leaf(L("Fusionner", "Merge"), null, L("Mettre bout à bout", "Join end to end"),
                Ff(fs => AppState.Jobs.Run(L($"Fusion · {fs.Count} pistes", $"Merge · {fs.Count} tracks"), (j, ct) => FfmpegEngine.ConcatAudio(j, ct, fs))), G.Merge));
        return menu;
    }

    // ================================================================ PDF

    private static List<MenuNode> PdfMenu(IReadOnlyList<string> files)
    {
        var menu = new List<MenuNode>
        {
            Cat(L("Convertir", "Convert"), G.Convert,
                Leaf("PNG", L("par page", "per page"), L("Une image PNG par page", "One PNG image per page"), fs => Each(fs, "→ PNG", (j, ct, f) => PdfEngine.ToImages(j, ct, f, "png"))),
                Leaf("JPG", L("par page", "per page"), L("Une image JPG par page", "One JPG image per page"), fs => Each(fs, "→ JPG", (j, ct, f) => PdfEngine.ToImages(j, ct, f, "jpg"))),
                Leaf("Word", "DOCX", L("Convertir en document Word", "Convert to a Word document"), fs => Requirements.WithWord(() => Each(fs, "→ DOCX", (j, ct, f) => Pipelines.ConvertDocument(j, ct, f, "docx")))),
                Leaf(L("Texte", "Text"), "TXT", L("Extraire le texte", "Extract the text"), fs => EachSync(fs, "→ TXT", (j, ct, f) => PdfEngine.ToText(j, ct, f)))),
            Cat(L("Compresser", "Compress"), G.Compress,
                Leaf(L("Léger", "Light"), L("sans perte", "lossless"), L("Optimiser sans perte", "Optimize losslessly"), fs => EachSync(fs, L("Compresser", "Compress"), (j, ct, f) => PdfEngine.CompressLossless(j, ct, f))),
                Leaf(L("Moyen", "Medium"), Tools.Ghostscript != null ? "ebook" : "144 dpi", L("Compresser (écran / ebook)", "Compress (screen / ebook)"), fs => Each(fs, L("Compresser", "Compress"), (j, ct, f) => PdfEngine.CompressStrong(j, ct, f, false))),
                Leaf(L("Fort", "Strong"), Tools.Ghostscript != null ? "screen" : "96 dpi", L("Compresser fortement", "Compress strongly"), fs => Each(fs, L("Compresser", "Compress"), (j, ct, f) => PdfEngine.CompressStrong(j, ct, f, true)))),
            Cat("Pages", G.Pages,
                Leaf(L("Séparer", "Split"), L("1 PDF/page", "1 PDF/page"), L("Un fichier par page", "One file per page"), fs => EachSync(fs, L("Séparer", "Split"), (j, ct, f) => PdfEngine.Split(j, ct, f))),
                Leaf(L("Extraire…", "Extract…"), null, L("Garder certaines pages", "Keep some pages"), fs => PickPages(fs, keep: true)),
                Leaf(L("Supprimer…", "Delete…"), null, L("Retirer certaines pages", "Remove some pages"), fs => PickPages(fs, keep: false)),
                Leaf(L("Inverser", "Reverse"), L("l'ordre", "order"), L("Inverser l'ordre des pages", "Reverse page order"), fs => EachSync(fs, L("Inverser", "Reverse"), (j, ct, f) =>
                    PdfEngine.Select(j, ct, f, Enumerable.Range(0, PdfEngine.PageCount(f)).Reverse().ToList(), L("inversé", "reversed"))))),
            Cat(L("Pivoter", "Rotate"), G.Rotate,
                Leaf("90° ↻", null, L("Pivoter toutes les pages à droite", "Rotate all pages clockwise"), fs => EachSync(fs, L("Pivoter", "Rotate"), (j, ct, f) => PdfEngine.Rotate(j, ct, f, 90))),
                Leaf("90° ↺", null, L("Pivoter toutes les pages à gauche", "Rotate all pages counter-clockwise"), fs => EachSync(fs, L("Pivoter", "Rotate"), (j, ct, f) => PdfEngine.Rotate(j, ct, f, -90))),
                Leaf("180°", null, L("Pivoter toutes les pages de 180°", "Rotate all pages 180°"), fs => EachSync(fs, L("Pivoter", "Rotate"), (j, ct, f) => PdfEngine.Rotate(j, ct, f, 180)))),
        };
        if (files.Count >= 2)
            menu.Add(Leaf(L("Fusionner", "Merge"), null, L("Assembler en un seul PDF", "Combine into a single PDF"),
                fs => AppState.Jobs.Run(L($"Fusion · {fs.Count} PDF", $"Merge · {fs.Count} PDFs"), (j, ct) => Pipelines.MergeToPdf(j, ct, fs)), G.Merge));
        return menu;
    }

    private static async void PickPages(IReadOnlyList<string> files, bool keep)
    {
        foreach (var f in files)
        {
            int count;
            try { count = PdfEngine.PageCount(f); }
            catch (Exception ex) { await Dialogs.Error("PDF", ex.Message); continue; }
            var text = await AppState.Host.Ask(keep ? L("Extraire des pages", "Extract pages") : L("Supprimer des pages", "Delete pages"),
                L($"{Name(f)} — {count} pages.\nExemple : 1-3, 5, 8-", $"{Name(f)} — {count} pages.\nExample: 1-3, 5, 8-"), keep ? "1" : "", keep ? L("Extraire", "Extract") : L("Supprimer", "Delete"),
                s => { PdfEngine.ParseRange(s, count); return null; });
            if (text == null) continue;
            var pages = PdfEngine.ParseRange(text, count);
            var selected = keep ? pages : Enumerable.Range(0, count).Except(pages).ToList();
            if (selected.Count == 0) { await Dialogs.Error("PDF", L("Il ne resterait aucune page.", "No page would be left.")); continue; }
            var label = keep ? $"pages {text.Replace(" ", "")}" : L("modifié", "edited");
            AppState.Jobs.Run($"{(keep ? L("Extraire", "Extract") : L("Supprimer", "Delete"))} · {Name(f)}", (j, ct) =>
            {
                PdfEngine.Select(j, ct, f, selected, label);
                return Task.CompletedTask;
            });
        }
    }

    // ================================================================ office & markdown

    private static List<MenuNode> OfficeMenu(IReadOnlyList<string> files, FileKind kind, string[] targets)
    {
        var shared = SharedExt(files);
        var menu = new List<MenuNode>
        {
            Cat(L("Convertir", "Convert"), G.Convert, targets.Where(t => t != shared && !(t == "html" && shared == "htm"))
                .Select(t => Leaf(Up(t), null, L($"Convertir en {Up(t)}", $"Convert to {Up(t)}"),
                    Office(kind, fs => Each(fs, $"→ {Up(t)}", (j, ct, f) => Pipelines.ConvertDocument(j, ct, f, t))))).ToArray()),
        };
        if (files.Count >= 2)
            menu.Add(Leaf(L("Fusionner", "Merge"), "PDF", L("Assembler en un seul PDF", "Combine into a single PDF"),
                Office(kind, fs => AppState.Jobs.Run(L($"Fusion · {fs.Count} fichiers", $"Merge · {fs.Count} files"), (j, ct) => Pipelines.MergeToPdf(j, ct, fs))), G.Merge));
        return menu;
    }

    private static List<MenuNode> PresentationMenu(IReadOnlyList<string> files)
    {
        var menu = OfficeMenu(files, FileKind.Presentation, new[] { "pdf", "pptx", "odp" });
        menu[0].Children.Add(Leaf("PNG", L("par diapo", "per slide"), L("Une image par diapositive", "One image per slide"),
            Office(FileKind.Presentation, fs => Each(fs, "→ PNG", (j, ct, f) => Pipelines.PresentationToImages(j, ct, f)))));
        return menu;
    }

    private static List<MenuNode> MarkdownMenu(IReadOnlyList<string> files) => new()
    {
        Cat(L("Convertir", "Convert"), G.Convert,
            Leaf("HTML", null, L("Convertir en page HTML", "Convert to an HTML page"), fs => Each(fs, "→ HTML", (j, ct, f) => Pipelines.ConvertMarkdown(j, ct, f, "html"))),
            Leaf("DOCX", null, L("Convertir en document Word", "Convert to a Word document"), Office(FileKind.Document, fs => Each(fs, "→ DOCX", (j, ct, f) => Pipelines.ConvertMarkdown(j, ct, f, "docx")))),
            Leaf("PDF", null, L("Convertir en PDF", "Convert to PDF"), Office(FileKind.Document, fs => Each(fs, "→ PDF", (j, ct, f) => Pipelines.ConvertMarkdown(j, ct, f, "pdf"))))),
    };

    private static List<MenuNode> MixedMenu(IReadOnlyList<string> files)
    {
        var kinds = files.Select(FileKinds.Of).Distinct().ToList();
        var menu = new List<MenuNode>();
        if (kinds.All(FileKinds.CanBecomePdf))
        {
            bool needsOffice = kinds.Any(k => k is FileKind.Document or FileKind.Spreadsheet or FileKind.Presentation or FileKind.Markdown);
            Action<IReadOnlyList<string>> merge = fs => AppState.Jobs.Run(L($"PDF · {fs.Count} fichiers", $"PDF · {fs.Count} files"), (j, ct) => Pipelines.MergeToPdf(j, ct, fs));
            Action<IReadOnlyList<string>> each = fs => Each(fs, "→ PDF", (j, ct, f) => FileKinds.Of(f) == FileKind.Pdf ? Task.CompletedTask : Pipelines.ToPdf(j, ct, f));
            if (files.Count >= 2) menu.Add(Leaf(L("Fusionner", "Merge"), L("en PDF", "to PDF"), L("Tout assembler en un PDF", "Combine everything into one PDF"), needsOffice ? Office(FileKind.Document, merge) : merge, G.Merge));
            menu.Add(Leaf(L("En PDF", "To PDF"), L("chacun", "each"), L("Convertir chaque fichier en PDF", "Convert each file to PDF"), needsOffice ? Office(FileKind.Document, each) : each, G.Pdf));
        }
        return menu;
    }

    // ================================================================ archive

    private static MenuNode ArchiveMenu() => Cat(L("Archiver", "Archive"), G.Archive,
        Leaf("ZIP", null, L("Compresser dans une archive ZIP", "Compress into a ZIP archive"), fs => AppState.Jobs.Run(fs.Count == 1 ? $"ZIP · {Name(fs[0])}" : L($"ZIP · {fs.Count} éléments", $"ZIP · {fs.Count} items"),
            (j, ct) => { Pipelines.Zip(j, ct, fs); return Task.CompletedTask; })),
        Leaf(L("Chemins", "Paths"), L("copier", "copy"), L("Copier les chemins dans le presse-papiers", "Copy the paths to the clipboard"), fs =>
        {
            try { AppState.Host.SetClipboard(string.Join(Environment.NewLine, fs)); AppState.Host.Notify(L("Chemins copiés", "Paths copied"), L($"{fs.Count} chemin(s) dans le presse-papiers.", $"{fs.Count} path(s) on the clipboard.")); }
            catch (Exception ex) { _ = Dialogs.Error(L("Presse-papiers", "Clipboard"), ex.Message); }
        }));
}
