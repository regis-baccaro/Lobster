using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using static Lobster.Loc;

namespace Lobster;

public sealed record MediaInfo(double Duration, int Width, int Height, bool HasVideo, bool HasAudio);

/// <summary>Audio/video work, done by a local ffmpeg.</summary>
public static class FfmpegEngine
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public static string F(double v) => v.ToString("0.###", Inv);

    private static string Exe => Tools.Ffmpeg ?? throw new ToolMissingException("ffmpeg",
        L("ffmpeg est introuvable. Ouvrez Lobster › Paramètres › Outils pour l'installer.", "ffmpeg was not found. Open Lobster › Settings › Engines to install it."));

    // ---------------------------------------------------------------- probing

    public static async Task<MediaInfo> Probe(string path, CancellationToken ct)
    {
        var probe = Tools.Ffprobe;
        if (probe != null)
        {
            var r = await ProcessRunner.Run(probe, new[]
            {
                "-v", "error", "-print_format", "json", "-show_format", "-show_streams", path
            }, ct);
            if (r.ExitCode == 0 && !string.IsNullOrWhiteSpace(r.StdOut))
                return ParseProbeJson(r.StdOut);
        }
        // Fallback: parse "ffmpeg -i" banner.
        var res = await ProcessRunner.Run(Exe, new[] { "-hide_banner", "-i", path }, ct);
        return ParseBanner(res.StdErrTail);
    }

    private static MediaInfo ParseProbeJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        double duration = 0;
        if (root.TryGetProperty("format", out var fmt) && fmt.TryGetProperty("duration", out var d))
            double.TryParse(d.GetString(), NumberStyles.Float, Inv, out duration);

        int w = 0, h = 0; bool hasVideo = false, hasAudio = false;
        if (root.TryGetProperty("streams", out var streams))
        {
            foreach (var s in streams.EnumerateArray())
            {
                var type = s.TryGetProperty("codec_type", out var t) ? t.GetString() : null;
                if (type == "audio") hasAudio = true;
                if (type != "video" || hasVideo) continue;
                if (s.TryGetProperty("disposition", out var disp) &&
                    disp.TryGetProperty("attached_pic", out var ap) && ap.GetInt32() == 1) continue;
                hasVideo = true;
                w = s.TryGetProperty("width", out var ww) ? ww.GetInt32() : 0;
                h = s.TryGetProperty("height", out var hh) ? hh.GetInt32() : 0;
                int rot = 0;
                if (s.TryGetProperty("tags", out var tags) && tags.TryGetProperty("rotate", out var rt))
                    int.TryParse(rt.GetString(), out rot);
                if (s.TryGetProperty("side_data_list", out var sdl))
                    foreach (var sd in sdl.EnumerateArray())
                        if (sd.TryGetProperty("rotation", out var r2) && r2.ValueKind == JsonValueKind.Number)
                            rot = r2.GetInt32();
                if (Math.Abs(rot) % 180 == 90) (w, h) = (h, w);
                if (duration <= 0 && s.TryGetProperty("duration", out var sdur))
                    double.TryParse(sdur.GetString(), NumberStyles.Float, Inv, out duration);
            }
        }
        return new MediaInfo(duration, w, h, hasVideo, hasAudio);
    }

    private static MediaInfo ParseBanner(string text)
    {
        double duration = 0;
        var m = Regex.Match(text, @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)");
        if (m.Success)
            duration = int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 +
                       double.Parse(m.Groups[3].Value, Inv);
        var v = Regex.Match(text, @"Video:.*?(\d{2,5})x(\d{2,5})");
        bool hasVideo = v.Success && !text.Contains("attached pic");
        return new MediaInfo(duration,
            v.Success ? int.Parse(v.Groups[1].Value) : 0,
            v.Success ? int.Parse(v.Groups[2].Value) : 0,
            hasVideo, text.Contains("Audio:"));
    }

    // ---------------------------------------------------------------- running

    public static async Task Run(Job job, IEnumerable<string> args, double duration, CancellationToken ct)
    {
        var all = new List<string> { "-hide_banner", "-nostdin", "-y", "-progress", "pipe:1", "-nostats" };
        all.AddRange(args);
        job.Report(duration > 0 ? 0 : -1);
        var res = await ProcessRunner.Run(Exe, all, ct, line =>
        {
            if (duration <= 0) return;
            if (line.StartsWith("out_time_us=") || line.StartsWith("out_time_ms="))
            {
                if (long.TryParse(line.AsSpan(12), out var us) && us > 0)
                    job.Report(Math.Min(0.99, us / 1_000_000.0 / duration));
            }
        });
        if (res.ExitCode != 0)
            throw new Exception("ffmpeg : " + ProcessRunner.Summarize(res.StdErrTail));
    }

    public sealed record Plan(List<string> Pre, List<string> Post, double? Duration = null);

    /// <summary>Probe → reserve output → run ffmpeg with the plan's arguments.</summary>
    public static async Task Transcode(Job job, CancellationToken ct, string src, string ext, string? suffix,
        Func<MediaInfo, Plan> build)
    {
        job.SetStatus(L("Analyse…", "Analyzing…"));
        var info = await Probe(src, ct);
        var plan = build(info);
        var outp = OutputPaths.Reserve(src, ext, suffix);
        try
        {
            job.SetStatus(L("Traitement…", "Processing…"));
            var args = new List<string>(plan.Pre) { "-i", src };
            args.AddRange(plan.Post);
            args.Add(outp);
            await Run(job, args, plan.Duration ?? info.Duration, ct);
            job.AddOutput(outp);
            job.SetStatus(SizeNote(src, outp));
        }
        catch
        {
            OutputPaths.TryDelete(outp);
            throw;
        }
    }

    public static string SizeNote(string src, string outp)
    {
        try
        {
            long a = new FileInfo(src).Length, b = new FileInfo(outp).Length;
            if (a <= 0) return L("Terminé", "Done");
            var delta = (b - a) * 100.0 / a;
            var pct = delta <= 0 ? $"−{Math.Abs(delta):0} %" : $"+{delta:0} %";
            return $"{FileKinds.HumanSize(a)} → {FileKinds.HumanSize(b)} ({pct})";
        }
        catch { return L("Terminé", "Done"); }
    }

    // ---------------------------------------------------------------- codecs

    public static List<string> VideoCodec(string ext, int crf = -1, string audioBitrate = "160k")
    {
        string c(int dflt) => (crf < 0 ? dflt : crf).ToString(Inv);
        return ext switch
        {
            "webm" => new() { "-c:v", "libvpx-vp9", "-crf", c(32), "-b:v", "0", "-row-mt", "1", "-deadline", "good", "-cpu-used", "4", "-c:a", "libopus", "-b:a", "128k", "-sn" },
            "avi" => new() { "-c:v", "mpeg4", "-q:v", "4", "-c:a", "libmp3lame", "-q:a", "4", "-sn" },
            "mkv" => new() { "-c:v", "libx264", "-preset", "medium", "-crf", c(23), "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", audioBitrate },
            "hevc" => new() { "-c:v", "libx265", "-preset", "medium", "-crf", c(26), "-tag:v", "hvc1", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", audioBitrate, "-movflags", "+faststart", "-sn" },
            _ => new() { "-c:v", "libx264", "-preset", "medium", "-crf", c(23), "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", audioBitrate, "-movflags", "+faststart", "-sn" },
        };
    }

    public static List<string> AudioCodec(string ext, string? bitrate = null) => ext switch
    {
        "mp3" => bitrate != null ? new() { "-c:a", "libmp3lame", "-b:a", bitrate } : new() { "-c:a", "libmp3lame", "-q:a", "2" },
        "m4a" or "aac" => new() { "-c:a", "aac", "-b:a", bitrate ?? "192k" },
        "wav" => new() { "-c:a", "pcm_s16le" },
        "aiff" => new() { "-c:a", "pcm_s16be" },
        "flac" => new() { "-c:a", "flac" },
        "ogg" => bitrate != null ? new() { "-c:a", "libvorbis", "-b:a", bitrate } : new() { "-c:a", "libvorbis", "-q:a", "5" },
        "opus" => new() { "-c:a", "libopus", "-b:a", bitrate ?? "128k" },
        "wma" => new() { "-c:a", "wmav2", "-b:a", bitrate ?? "192k" },
        _ => new() { "-c:a", "libmp3lame", "-q:a", "2" },
    };

    /// <summary>Container to keep when editing a video in place.</summary>
    public static string EditExt(string src)
    {
        var e = FileKinds.Ext(src);
        return e is "mp4" or "mov" or "mkv" or "webm" or "m4v" ? e : "mp4";
    }

    public static string AudioEditExt(string src)
    {
        var e = FileKinds.Ext(src);
        return e is "mp3" or "m4a" or "wav" or "flac" or "ogg" or "opus" or "aiff" or "wma" ? e : "mp3";
    }

    public static string ScaleFilter(MediaInfo info, int target) =>
        info.Width >= info.Height
            ? $"scale=-2:'min({target},ih)'"
            : $"scale='min({target},iw)':-2";

    // ---------------------------------------------------------------- operations

    public static Task ConvertVideo(Job job, CancellationToken ct, string src, string target)
    {
        if (target == "gif")
            return Transcode(job, ct, src, "gif", null, _ => new Plan(new(), new()
            {
                "-vf", "fps=12,scale='min(480,iw)':-1:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4",
                "-loop", "0"
            }));

        if (target == "mkv")
            return RemuxOrTranscode(job, ct, src, "mkv");

        var ext = target == "hevc" ? "mp4" : target;
        var suffix = target == "hevc" ? "HEVC" : null;
        return Transcode(job, ct, src, ext, suffix, _ =>
            new Plan(new(), Concat(new List<string> { "-map_metadata", "0" }, VideoCodec(target))));
    }

    private static async Task RemuxOrTranscode(Job job, CancellationToken ct, string src, string ext)
    {
        try
        {
            await Transcode(job, ct, src, ext, null, _ => new Plan(new(), new() { "-map", "0", "-c", "copy" }));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            job.SetStatus(L("Copie directe impossible, réencodage…", "Direct copy failed, re-encoding…"));
            await Transcode(job, ct, src, ext, null, _ => new Plan(new(), VideoCodec(ext)));
        }
    }

    public static Task CompressVideo(Job job, CancellationToken ct, string src, int crf, int maxHeight, string audio)
    {
        var ext = EditExt(src) == "webm" ? "webm" : "mp4";
        return Transcode(job, ct, src, ext, L("compressé", "compressed"), info =>
        {
            var post = new List<string>();
            if (maxHeight > 0) post.AddRange(new[] { "-vf", ScaleFilter(info, maxHeight) });
            post.AddRange(VideoCodec(ext, ext == "webm" ? crf + 8 : crf, audio));
            return new Plan(new(), post);
        });
    }

    public static Task CompressVideoToSize(Job job, CancellationToken ct, string src, double megabytes)
    {
        return Transcode(job, ct, src, "mp4", $"{megabytes:0} {L("Mo", "MB")}", info =>
        {
            if (info.Duration <= 0) throw new Exception(L("Durée inconnue : impossible de viser une taille.", "Unknown duration: cannot target a size."));
            double totalK = megabytes * 8 * 1024 * 0.94 / info.Duration;
            double audioK = info.HasAudio ? (totalK > 600 ? 128 : 64) : 0;
            double videoK = Math.Max(120, totalK - audioK);
            var post = new List<string>();
            int height = videoK < 600 ? 480 : videoK < 1400 ? 720 : 1080;
            post.AddRange(new[] { "-vf", ScaleFilter(info, height) });
            post.AddRange(new[]
            {
                "-c:v", "libx264", "-preset", "medium", "-b:v", $"{videoK:0}k",
                "-maxrate", $"{videoK * 1.3:0}k", "-bufsize", $"{videoK * 2:0}k", "-pix_fmt", "yuv420p", "-sn",
                "-movflags", "+faststart"
            });
            if (info.HasAudio) post.AddRange(new[] { "-c:a", "aac", "-b:a", $"{audioK:0}k" });
            return new Plan(new(), post);
        });
    }

    public static Task ResizeVideo(Job job, CancellationToken ct, string src, int height)
    {
        var ext = EditExt(src);
        return Transcode(job, ct, src, ext, $"{height}p", info =>
            new Plan(new(), Concat(new List<string> { "-vf", ScaleFilter(info, height) }, VideoCodec(ext))));
    }

    public static Task ResizeVideoPercent(Job job, CancellationToken ct, string src, int percent)
    {
        var ext = EditExt(src);
        double f = percent / 100.0;
        return Transcode(job, ct, src, ext, $"{percent} %", _ =>
            new Plan(new(), Concat(new List<string> { "-vf", $"scale=trunc(iw*{F(f)}/2)*2:trunc(ih*{F(f)}/2)*2" }, VideoCodec(ext))));
    }

    public static Task CropVideo(Job job, CancellationToken ct, string src, double fx, double fy, double fw, double fh)
    {
        var ext = EditExt(src);
        return Transcode(job, ct, src, ext, L("rogné", "cropped"), info =>
        {
            if (info.Width <= 0) throw new Exception(L("Dimensions vidéo inconnues.", "Unknown video dimensions."));
            int Even(double v) => Math.Max(2, (int)Math.Round(v / 2) * 2);
            int w = Even(fw * info.Width), h = Even(fh * info.Height);
            int x = Even(fx * info.Width), y = Even(fy * info.Height);
            w = Math.Min(w, info.Width - x); h = Math.Min(h, info.Height - y);
            w -= w % 2; h -= h % 2;
            return new Plan(new(), Concat(new List<string> { "-vf", $"crop={w}:{h}:{x}:{y}" }, VideoCodec(ext)));
        });
    }

    public static Task Trim(Job job, CancellationToken ct, string src, double start, double end, bool fast, bool isVideo)
    {
        var ext = isVideo ? EditExt(src) : AudioEditExt(src);
        double dur = Math.Max(0.05, end - start);
        return Transcode(job, ct, src, ext, L("extrait", "clip"), _ =>
        {
            var pre = new List<string> { "-ss", F(start) };
            var post = new List<string> { "-t", F(dur) };
            if (fast) post.AddRange(new[] { "-map", "0", "-c", "copy", "-avoid_negative_ts", "make_zero" });
            else post.AddRange(isVideo ? VideoCodec(ext) : Concat(new List<string> { "-vn" }, AudioCodec(ext)));
            return new Plan(pre, post, dur);
        });
    }

    public static Task RotateVideo(Job job, CancellationToken ct, string src, string filter, string suffix)
    {
        var ext = EditExt(src);
        return Transcode(job, ct, src, ext, suffix, _ =>
            new Plan(new(), Concat(new List<string> { "-vf", filter }, VideoCodec(ext))));
    }

    public static Task Mute(Job job, CancellationToken ct, string src) =>
        Transcode(job, ct, src, EditExt(src), L("sans son", "no sound"), _ => new Plan(new(), new() { "-map", "0:v", "-c", "copy", "-an" }));

    public static Task Speed(Job job, CancellationToken ct, string src, double factor, bool isVideo)
    {
        var ext = isVideo ? EditExt(src) : AudioEditExt(src);
        return Transcode(job, ct, src, ext, $"×{factor.ToString("0.##", CultureInfo.GetCultureInfo(IsEnglish ? "en-US" : "fr-FR"))}", info =>
        {
            var post = new List<string>();
            if (isVideo && info.HasVideo) post.AddRange(new[] { "-filter:v", $"setpts=PTS/{F(factor)}" });
            if (info.HasAudio) post.AddRange(new[] { "-filter:a", $"atempo={F(factor)}" });
            else post.Add("-an");
            post.AddRange(isVideo ? VideoCodec(ext) : AudioCodec(ext));
            return new Plan(new(), post, info.Duration / factor);
        });
    }

    public static Task Capture(Job job, CancellationToken ct, string src)
    {
        return Transcode(job, ct, src, "png", L("capture", "frame"), info =>
            new Plan(new() { "-ss", F(info.Duration > 0 ? info.Duration / 2 : 0) }, new() { "-frames:v", "1" }, 0));
    }

    public static Task ExtractAudio(Job job, CancellationToken ct, string src, string ext) =>
        Transcode(job, ct, src, ext, null, info =>
        {
            if (!info.HasAudio) throw new Exception(L("Cette vidéo n'a pas de piste audio.", "This video has no audio track."));
            return new Plan(new(), Concat(new List<string> { "-vn", "-map_metadata", "0" }, AudioCodec(ext)));
        });

    public static Task ConvertAudio(Job job, CancellationToken ct, string src, string ext) =>
        Transcode(job, ct, src, ext, null, _ =>
            new Plan(new(), Concat(new List<string> { "-vn", "-map_metadata", "0" }, AudioCodec(ext))));

    public static Task CompressAudio(Job job, CancellationToken ct, string src, string bitrate)
    {
        var e = FileKinds.Ext(src);
        var ext = e is "mp3" or "m4a" or "ogg" or "opus" or "wma" ? e : "mp3";
        return Transcode(job, ct, src, ext, bitrate.Replace("k", " kbps"), _ =>
            new Plan(new(), Concat(new List<string> { "-vn", "-map_metadata", "0" }, AudioCodec(ext, bitrate))));
    }

    public static Task AudioFilter(Job job, CancellationToken ct, string src, bool isVideo, string suffix,
        Func<MediaInfo, string> filter, bool mono = false)
    {
        var ext = isVideo ? EditExt(src) : AudioEditExt(src);
        return Transcode(job, ct, src, ext, suffix, info =>
        {
            if (!info.HasAudio) throw new Exception(L("Aucune piste audio.", "No audio track."));
            var post = new List<string> { "-af", filter(info) };
            if (mono) post.AddRange(new[] { "-ac", "1" });
            if (isVideo)
            {
                post.AddRange(new[] { "-c:v", "copy" });
                post.AddRange(ext == "webm" ? new[] { "-c:a", "libopus", "-b:a", "160k" } : new[] { "-c:a", "aac", "-b:a", "192k" });
            }
            else
            {
                post.Add("-vn");
                post.AddRange(AudioCodec(ext));
            }
            return new Plan(new(), post);
        });
    }

    public static Task GifToVideo(Job job, CancellationToken ct, string src, string ext) =>
        Transcode(job, ct, src, ext, null, _ => new Plan(new(), ext == "webm"
            ? new() { "-c:v", "libvpx-vp9", "-crf", "30", "-b:v", "0", "-pix_fmt", "yuv420p", "-an" }
            : new() { "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2", "-c:v", "libx264", "-crf", "20", "-pix_fmt", "yuv420p", "-movflags", "+faststart", "-an" }));

    public static async Task<string> ExtractFrame(string src, double at, CancellationToken ct)
    {
        var tmp = Path.Combine(OutputPaths.TempDir(), "frame.png");
        var res = await ProcessRunner.Run(Exe, new[]
        {
            "-hide_banner", "-nostdin", "-y", "-ss", F(at), "-i", src, "-frames:v", "1",
            "-vf", "scale='min(1600,iw)':-2", tmp
        }, ct);
        if (res.ExitCode != 0 || !File.Exists(tmp))
            throw new Exception(L("Impossible d'extraire une image de la vidéo : ", "Could not extract a frame from the video: ") + ProcessRunner.Summarize(res.StdErrTail));
        return tmp;
    }

    // ---------------------------------------------------------------- merging

    public static async Task ConcatVideos(Job job, CancellationToken ct, IReadOnlyList<string> files)
    {
        job.SetStatus(L("Analyse…", "Analyzing…"));
        var infos = new List<MediaInfo>();
        foreach (var f in files) infos.Add(await Probe(f, ct));
        var first = infos.FirstOrDefault(i => i.HasVideo) ?? throw new Exception(L("Aucune piste vidéo.", "No video track."));
        int W = Math.Max(2, first.Width - first.Width % 2), H = Math.Max(2, first.Height - first.Height % 2);

        var args = new List<string>();
        foreach (var f in files) { args.Add("-i"); args.Add(f); }
        var graph = new StringBuilder();
        for (int i = 0; i < files.Count; i++)
        {
            graph.Append($"[{i}:v:0]scale={W}:{H}:force_original_aspect_ratio=decrease,pad={W}:{H}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,format=yuv420p[v{i}];");
            if (infos[i].HasAudio)
                graph.Append($"[{i}:a:0]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo[a{i}];");
            else
                graph.Append($"anullsrc=r=48000:cl=stereo,atrim=duration={F(Math.Max(0.1, infos[i].Duration))}[a{i}];");
        }
        for (int i = 0; i < files.Count; i++) graph.Append($"[v{i}][a{i}]");
        graph.Append($"concat=n={files.Count}:v=1:a=1[v][a]");

        args.AddRange(new[] { "-filter_complex", graph.ToString(), "-map", "[v]", "-map", "[a]" });
        args.AddRange(new[] { "-c:v", "libx264", "-preset", "medium", "-crf", "22", "-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart" });

        var outp = OutputPaths.Reserve(files[0], "mp4", L("fusion", "merged"));
        try
        {
            args.Add(outp);
            job.SetStatus(L("Fusion…", "Merging…"));
            await Run(job, args, infos.Sum(i => i.Duration), ct);
            job.AddOutput(outp);
            job.SetStatus(L($"{files.Count} vidéos fusionnées", $"{files.Count} videos merged"));
        }
        catch { OutputPaths.TryDelete(outp); throw; }
    }

    public static async Task ConcatAudio(Job job, CancellationToken ct, IReadOnlyList<string> files)
    {
        job.SetStatus(L("Analyse…", "Analyzing…"));
        double total = 0;
        foreach (var f in files) total += (await Probe(f, ct)).Duration;
        var ext = FileKinds.Ext(files[0]) is "wav" or "flac" or "m4a" or "ogg" or "opus" ? FileKinds.Ext(files[0]) : "mp3";

        var args = new List<string>();
        foreach (var f in files) { args.Add("-i"); args.Add(f); }
        var graph = new StringBuilder();
        for (int i = 0; i < files.Count; i++)
            graph.Append($"[{i}:a:0]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo[a{i}];");
        for (int i = 0; i < files.Count; i++) graph.Append($"[a{i}]");
        graph.Append($"concat=n={files.Count}:v=0:a=1[a]");
        args.AddRange(new[] { "-filter_complex", graph.ToString(), "-map", "[a]" });
        args.AddRange(AudioCodec(ext));

        var outp = OutputPaths.Reserve(files[0], ext, L("fusion", "merged"));
        try
        {
            args.Add(outp);
            await Run(job, args, total, ct);
            job.AddOutput(outp);
            job.SetStatus(L($"{files.Count} pistes fusionnées", $"{files.Count} tracks merged"));
        }
        catch { OutputPaths.TryDelete(outp); throw; }
    }

    private static List<string> Concat(List<string> a, IEnumerable<string> b)
    {
        a.AddRange(b);
        return a;
    }
}
