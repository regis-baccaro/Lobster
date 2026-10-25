using System;
using System.IO;
using System.Threading.Tasks;
using static Lobster.Loc;

namespace Lobster;

/// <summary>Makes sure the right local engine exists before running an action.</summary>
public static class Requirements
{
    private static IHost Host => AppState.Host;

    public static async void WithFfmpeg(Action go)
    {
        if (Tools.Ffmpeg != null) { go(); return; }

        if (OperatingSystem.IsWindows())
        {
            var choice = await Host.Show(L("ffmpeg nécessaire", "ffmpeg required"),
                L("Lobster utilise ffmpeg (gratuit, open source, 100 % local) pour l'audio et la vidéo.\n\n" +
                  "Installer ffmpeg maintenant avec winget ? L'action reprendra automatiquement ensuite.",
                  "Lobster uses ffmpeg (free, open source, 100% local) for audio and video.\n\n" +
                  "Install ffmpeg now with winget? The action will resume automatically afterwards."),
                new[] { L("Installer", "Install"), L("Choisir ffmpeg.exe…", "Choose ffmpeg.exe…"), L("Annuler", "Cancel") }, "video");
            if (choice == 0) InstallFfmpeg(go);
            else if (choice == 1 && await BrowseFfmpeg()) go();
            return;
        }

        string cmd = OperatingSystem.IsMacOS() ? "brew install ffmpeg" : Tools.LinuxInstallCommand("ffmpeg", "ffmpeg", "ffmpeg");
        var c = await Host.Show(L("ffmpeg nécessaire", "ffmpeg required"),
            L($"Lobster utilise ffmpeg (gratuit, open source, 100 % local) pour l'audio et la vidéo.\n\nInstallez-le dans un terminal :\n\n    {cmd}\n\nPuis relancez l'action.",
              $"Lobster uses ffmpeg (free, open source, 100% local) for audio and video.\n\nInstall it from a terminal:\n\n    {cmd}\n\nThen run the action again."),
            new[] { L("Copier la commande", "Copy command"), L("Choisir ffmpeg…", "Choose ffmpeg…"), L("Fermer", "Close") }, "video");
        if (c == 0) Host.SetClipboard(cmd);
        else if (c == 1 && await BrowseFfmpeg()) go();
    }

    public static async Task<bool> BrowseFfmpeg()
    {
        var file = await Host.PickFile(L("Choisir ffmpeg", "Choose ffmpeg"), OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        if (file == null) return false;
        AppState.Settings.FfmpegPath = file;
        AppState.Settings.Save();
        Tools.Reset();
        return Tools.Ffmpeg != null;
    }

    public static void InstallFfmpeg(Action? then)
    {
        AppState.Jobs.Run(L("Installation de ffmpeg", "Installing ffmpeg"), async (j, ct) =>
        {
            j.Report(-1, "winget…");
            var winget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", "winget.exe");
            if (!File.Exists(winget))
                throw new Exception(L("winget est introuvable. Téléchargez ffmpeg sur gyan.dev/ffmpeg/builds puis indiquez ffmpeg.exe dans les paramètres.",
                                      "winget was not found. Download ffmpeg from gyan.dev/ffmpeg/builds, then point to ffmpeg.exe in the settings."));
            var r = await ProcessRunner.Run(winget, new[]
            {
                "install", "--id", "Gyan.FFmpeg", "-e", "--source", "winget",
                "--accept-source-agreements", "--accept-package-agreements", "--disable-interactivity"
            }, ct);
            Tools.Reset();
            if (Tools.Ffmpeg == null)
                throw new Exception(L("L'installation n'a pas abouti. ", "Installation did not complete. ") + ProcessRunner.Summarize(r.StdOut + "\n" + r.StdErrTail));
            j.SetStatus(L("ffmpeg installé", "ffmpeg installed"));
            if (then != null) Ui.Post(then);
        });
    }

    public static async void WithOffice(FileKind kind, Action go)
    {
        if (OfficeEngine.Available(kind)) { go(); return; }
        string extra = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? L("LibreOffice est gratuit ; Lobster l'utilise en arrière-plan, sans l'ouvrir.",
                "LibreOffice is free; Lobster runs it in the background without opening it.")
            : L("Dans un terminal : ", "From a terminal: ") + Tools.LinuxInstallCommand("libreoffice", "libreoffice", "libreoffice-fresh");
        var choice = await Host.Show(L("Suite bureautique nécessaire", "Office suite required"),
            OfficeEngine.MissingMessage + "\n\n" + extra,
            new[] { L("Télécharger LibreOffice", "Download LibreOffice"), L("Fermer", "Close") }, "document");
        if (choice == 0) Host.OpenUrl("https://www.libreoffice.org/download/download-libreoffice/");
    }

    public static void WithWord(Action go)
    {
        if (Tools.HasWord) { go(); return; }
        _ = Dialogs.Error(L("Microsoft Word nécessaire", "Microsoft Word required"),
            L("La conversion d'un PDF en document Word utilise Microsoft Word, qui n'est pas installé sur cet ordinateur.\n\n" +
              "Astuce : « Texte » extrait le contenu brut sans Word.",
              "Converting a PDF to a Word document uses Microsoft Word, which is not installed on this computer.\n\n" +
              "Tip: “Text” extracts the raw content without Word."));
    }
}
