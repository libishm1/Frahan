#nullable disable
using System;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper.Kernel;

namespace Frahan.GH.ScanIngest;

// =============================================================================
// AsyncScanComponent — shared non-blocking base for the heavy scan-ingest
// components (Load Cloud, Read LAS Cloud, Estimate Cloud Normals, Scan
// Reconstruct). Mirrors the KintsugiAssemblyComponent async behaviour so the
// Grasshopper canvas never freezes on a multi-million-point read or a native
// reconstruction:
//
//   * A "Run" gate (default false). With Run unwired/false the component does
//     NO heavy work, so opening a definition never auto-triggers a giant load
//     or reconstruction (the cause of the open-time freeze/crash). The user
//     toggles Run to execute.
//   * The heavy work runs on a background Task. The canvas stays navigable;
//     the component Message shows progress.
//   * When the Task finishes it schedules a re-solve (GH_Document.ScheduleSolution)
//     so the result reaches the canvas on the UI thread.
//   * Setting Run=false cancels any in-flight Task (cooperative; native calls
//     that are already running finish, then their result is discarded).
//
// State is per-instance (not static), so several scan components can run
// concurrently without clobbering each other.
//
// Subclasses implement four hooks; the two-pass state machine lives here.
// =============================================================================

/// <typeparam name="TSnapshot">Immutable inputs captured on the UI thread.</typeparam>
/// <typeparam name="TPayload">Result produced on the background thread.</typeparam>
public abstract class AsyncScanComponent<TSnapshot, TPayload> : GH_Component
    where TSnapshot : class
    where TPayload : class
{
    protected AsyncScanComponent(string name, string nickname, string description,
        string category, string subCategory)
        : base(name, nickname, description, category, subCategory)
    {
    }

    // -------- per-instance async state --------
    private readonly object _gate = new object();
    private Task _task;
    private CancellationTokenSource _cts;
    private volatile string _progress = "";
    private TPayload _payload;
    private string _error;
    private bool _ready;

    // -------- subclass hooks --------

    /// <summary>
    /// Read inputs on the UI thread. Contract:
    ///   * Always set <paramref name="run"/> from the Run input first.
    ///   * If run is false: set snapshot=null and return true (caller idles).
    ///   * If run is true: validate + capture an owned snapshot. On failure,
    ///     add a runtime message and return false. On success return true.
    /// Capture must be deep enough that the background Task never reads live
    /// Grasshopper data (duplicate clouds / copy lists here).
    /// </summary>
    protected abstract bool TryRead(IGH_DataAccess da, out bool run, out TSnapshot snapshot);

    /// <summary>Heavy work on the background thread. Honor the token; report progress.</summary>
    protected abstract TPayload Compute(TSnapshot snapshot, CancellationToken token, Action<string> progress);

    /// <summary>Emit a successful payload on the UI thread.</summary>
    protected abstract void EmitResult(IGH_DataAccess da, TPayload payload);

    /// <summary>Emit empty geometry outputs plus a status string (idle / computing / error).</summary>
    protected abstract void EmitIdle(IGH_DataAccess da, string message);

    // -------- two-pass state machine --------

    protected override void SolveInstance(IGH_DataAccess da)
    {
        bool readOk = TryRead(da, out bool run, out TSnapshot snapshot);

        if (!run)
        {
            bool wasRunning = CancelIfRunning();
            EmitIdle(da, wasRunning
                ? "Run is false. In-flight compute cancelled (the canvas was never frozen)."
                : "Run is false. Set Run = true to execute.");
            return;
        }
        if (!readOk) return; // subclass already reported the validation error

        lock (_gate)
        {
            if (_ready)
            {
                TPayload payload = _payload;
                string error = _error;
                _payload = null; _error = null; _ready = false;
                if (error != null)
                {
                    Message = "error";
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error);
                    EmitIdle(da, error);
                    return;
                }
                Message = "done";
                EmitResult(da, payload);
                return;
            }
            if (_task != null && !_task.IsCompleted)
            {
                // A compute is already in flight; echo progress and stay empty.
                Message = _progress;
                EmitIdle(da, "Computing... " + _progress +
                              "  (canvas stays navigable; set Run = false to cancel).");
                return;
            }
        }

        // No result, no task in flight: start one.
        var doc = OnPingDocument();
        Guid iguid = InstanceGuid;
        try { _cts?.Cancel(); } catch { }
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        _progress = "starting...";
        Message = _progress;
        _ready = false;
        TSnapshot snap = snapshot;

        _task = Task.Run(() =>
        {
            TPayload payload = null;
            string error = null;
            try
            {
                payload = Compute(snap, token, label =>
                {
                    _progress = label;
                    try
                    {
                        doc?.ScheduleSolution(50, d =>
                        {
                            if (d?.FindComponent(iguid) is GH_Component c) c.Message = label;
                        });
                    }
                    catch { }
                });
            }
            catch (OperationCanceledException) { /* discarded below */ }
            catch (Exception ex) { error = $"{ex.GetType().Name}: {ex.Message}"; }
            finally
            {
                bool cancelled = token.IsCancellationRequested;
                lock (_gate)
                {
                    if (cancelled)
                    {
                        _payload = null; _error = null; _ready = false;
                    }
                    else
                    {
                        _payload = payload; _error = error; _ready = true;
                    }
                }
                try
                {
                    doc?.ScheduleSolution(10, d =>
                    {
                        if (d?.FindComponent(iguid) is GH_Component c) c.ExpireSolution(true);
                    });
                }
                catch { }
            }
        }, token);

        EmitIdle(da, "Started. Canvas stays navigable; watch the component label for progress. " +
                     "Results pop in automatically when ready.");
    }

    /// <summary>Cancel any in-flight Task and drop stale results. Returns true if a Task was running.</summary>
    private bool CancelIfRunning()
    {
        lock (_gate)
        {
            bool running = _task != null && !_task.IsCompleted;
            if (running)
            {
                try { _cts?.Cancel(); } catch { }
                Message = "cancelling...";
            }
            _payload = null; _error = null; _ready = false; _progress = "";
            return running;
        }
    }
}
