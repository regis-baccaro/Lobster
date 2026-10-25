using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Lobster;

public enum JobState { Queued, Running, Done, Failed, Cancelled }

public sealed class Job : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get; }
    public CancellationTokenSource Cts { get; } = new();
    internal Func<Job, CancellationToken, Task> Work { get; }
    public List<string> Outputs { get; } = new();
    internal bool Reported { get; set; }

    private string _status = Loc.L("En attente…", "Queued…");
    private double _progress = -1;
    private JobState _state = JobState.Queued;
    private string? _error;

    public Job(string title, Func<Job, CancellationToken, Task> work)
    {
        Title = title;
        Work = work;
    }

    public string Status { get => _status; set => Set(ref _status, value); }
    public double Progress { get => _progress; set { Set(ref _progress, value); Notify(nameof(ProgressPercent)); Notify(nameof(IsIndeterminate)); } }
    public double ProgressPercent => Math.Max(0, Math.Min(100, _progress * 100));
    public bool IsIndeterminate => _progress < 0 && _state == JobState.Running;
    public string? Error { get => _error; set { Set(ref _error, value); Notify(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrEmpty(_error);

    public JobState State
    {
        get => _state;
        set
        {
            Set(ref _state, value);
            Notify(nameof(IsActive)); Notify(nameof(IsDone)); Notify(nameof(IsFinished));
            Notify(nameof(IsIndeterminate)); Notify(nameof(StateGlyph)); Notify(nameof(HasOutputs));
        }
    }

    internal static string Running => Loc.L("En cours…", "Working…");

    public bool IsActive => _state is JobState.Queued or JobState.Running;
    public bool IsDone => _state == JobState.Done;
    public bool IsFinished => !IsActive;
    public bool HasOutputs => _state == JobState.Done && Outputs.Count > 0;

    public string StateGlyph => _state switch
    {
        JobState.Done => "",
        JobState.Failed => "",
        JobState.Cancelled => "",
        _ => ""
    };

    /// <summary>Thread-safe progress report (0..1, or negative for indeterminate).</summary>
    public void Report(double progress, string? status = null) => Ui.Post(() =>
    {
        Progress = progress;
        if (status != null) Status = status;
    });

    public void SetStatus(string status) => Ui.Post(() => Status = status);

    public void AddOutput(string path)
    {
        lock (Outputs) Outputs.Add(path);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Notify(name!);
    }

    private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class JobManager
{
    public ObservableCollection<Job> All { get; } = new();
    private readonly SemaphoreSlim _gate;

    public event Action? Changed;
    public event Action<IReadOnlyList<Job>>? BatchFinished;

    public JobManager(int parallel) => _gate = new SemaphoreSlim(Math.Max(1, parallel));

    public bool AnyActive => All.Any(j => j.IsActive);

    public Job Run(string title, Func<Job, CancellationToken, Task> work)
    {
        var job = new Job(title, work);
        All.Insert(0, job);
        while (All.Count > 60 && All[^1].IsFinished) All.RemoveAt(All.Count - 1);
        Changed?.Invoke();
        _ = Execute(job);
        return job;
    }

    private async Task Execute(Job job)
    {
        try { await _gate.WaitAsync(job.Cts.Token); }
        catch (OperationCanceledException)
        {
            Finish(job, JobState.Cancelled, Loc.L("Annulé", "Cancelled"));
            return;
        }

        try
        {
            job.State = JobState.Running;
            job.Status = Job.Running;
            await Task.Run(() => job.Work(job, job.Cts.Token));
            job.Progress = 1;
            Finish(job, JobState.Done, job.Status == Job.Running ? Loc.L("Terminé", "Done") : job.Status);
        }
        catch (OperationCanceledException)
        {
            foreach (var o in job.Outputs) OutputPaths.TryDelete(o);
            Finish(job, JobState.Cancelled, Loc.L("Annulé", "Cancelled"));
        }
        catch (Exception ex)
        {
            Log.Error($"{job.Title}: {ex}");
            job.Error = Friendly(ex);
            Finish(job, JobState.Failed, Loc.L("Échec", "Failed"));
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Finish(Job job, JobState state, string status)
    {
        job.State = state;
        job.Status = status;
        Changed?.Invoke();
        if (!AnyActive)
        {
            var recent = All.Where(j => j.IsFinished && !j.Reported).ToList();
            foreach (var j in recent) j.Reported = true;
            if (recent.Count > 0) BatchFinished?.Invoke(recent);
        }
    }

    public void ClearFinished()
    {
        foreach (var j in All.Where(j => j.IsFinished).ToList()) All.Remove(j);
        Changed?.Invoke();
    }

    private static string Friendly(Exception ex) => ex switch
    {
        ToolMissingException t => t.Message,
        UnauthorizedAccessException => Loc.L("Accès refusé : le fichier est peut-être en lecture seule ou ouvert ailleurs.", "Access denied: the file may be read-only or open elsewhere."),
        System.IO.IOException io when io.Message.Contains("used by another process") => Loc.L("Le fichier est utilisé par une autre application.", "The file is in use by another application."),
        _ => ex.Message
    };
}

public sealed class ToolMissingException : Exception
{
    public string Tool { get; }
    public ToolMissingException(string tool, string message) : base(message) => Tool = tool;
}
