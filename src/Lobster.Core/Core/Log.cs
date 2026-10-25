using System;
using System.IO;

namespace Lobster;

public static class Log
{
    private static readonly object Gate = new();
    public static string FilePath => Path.Combine(Settings.AppDataDir, "lobster.log");

    public static void Info(string msg) => Write("INFO ", msg);
    public static void Error(string msg) => Write("ERROR", msg);

    private static void Write(string level, string msg)
    {
        try
        {
            lock (Gate)
            {
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > 2_000_000) fi.Delete();
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {msg}{Environment.NewLine}");
            }
        }
        catch { /* logging must never crash the app */ }
    }
}
