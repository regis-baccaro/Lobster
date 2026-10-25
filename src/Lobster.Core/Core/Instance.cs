using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lobster;

/// <summary>
/// One Lobster per user session. Extra launches (context menu, Send To, "Open with")
/// hand their files to the running instance over a named pipe and exit.
/// On Linux .NET maps the pipe to a Unix domain socket.
/// </summary>
public static class SingleInstance
{
    private static string PipeName => "FreiaTech.Lobster." + Environment.UserName;

    /// <summary>Returns true when a running instance received the message.</summary>
    public static bool Send(string[] args, Action? beforeSend = null, int timeoutMs = 3000)
    {
        try
        {
            beforeSend?.Invoke();
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeoutMs);
            var payload = string.Join("\n", args.Length == 0 ? new[] { "--show" } : args) + "\n";
            var bytes = Encoding.UTF8.GetBytes(payload);
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
            return true;
        }
        catch (Exception ex)
        {
            if (timeoutMs >= 1000) Log.Error("pipe send: " + ex.Message);
            return false;
        }
    }

    public static void Listen(Action<string[]> onMessage)
    {
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync();
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var text = await reader.ReadToEndAsync();
                    var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (lines.Length > 0) onMessage(lines);
                }
                catch (Exception ex)
                {
                    Log.Error("pipe listen: " + ex.Message);
                    await Task.Delay(500);
                }
            }
        });
    }
}

/// <summary>Collects files arriving in bursts (one launch per selected file) into one menu.</summary>
public sealed class FileBatcher
{
    private readonly List<string> _pending = new();
    private readonly object _gate = new();
    private readonly Timer _timer;
    private readonly Action<IReadOnlyList<string>> _flush;

    public FileBatcher(Action<IReadOnlyList<string>> flush)
    {
        _flush = flush;
        _timer = new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Add(IEnumerable<string> files)
    {
        lock (_gate)
        {
            _pending.AddRange(files.Select(Normalize));
            _timer.Change(350, Timeout.Infinite);
        }
    }

    /// <summary>Accepts plain paths and file:// URIs (Linux file managers often pass URIs).</summary>
    private static string Normalize(string f)
    {
        if (f.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            try { return new Uri(f).LocalPath; } catch { }
        }
        return f;
    }

    private void Fire()
    {
        List<string> files;
        lock (_gate)
        {
            files = _pending.Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(f => File.Exists(f) || Directory.Exists(f)).ToList();
            _pending.Clear();
        }
        if (files.Count > 0) Ui.Post(() => _flush(files));
    }
}
