using Arena.Engine;

namespace Arena.Rendering;

public sealed record StrideRunOptions(
    bool Headless = false, bool Manual = false, int InitialView = 0,
    int? ExitAfterFrames = null, string? Screenshot = null, string Endpoint = "127.0.0.1:50051");

public sealed record BackendMetrics(long Images, double ImageMilliseconds, long PhysicsSteps, double PhysicsMilliseconds, string Device);

/// <summary>Owns a single Stride thread and exposes a bounded command queue to the RPC host.</summary>
public sealed class StrideHost : IAsyncDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread thread;
    private int disposed;

    public StrideHost(StrideRunOptions options, Func<ICharacterWorld, IObservationRenderer, GameSimulation> createSimulation)
    {
        thread = new Thread(() => Run(options, createSimulation)) { IsBackground = true, Name = "Arena.Stride" };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public SimulationDispatcher Dispatcher { get; } = new();
    public Task Ready => ready.Task;
    public Task Completion => finished.Task;
    public BackendMetrics Metrics { get; private set; } = new(0, 0, 0, 0, "starting");

    private void Run(StrideRunOptions options, Func<ICharacterWorld, IObservationRenderer, GameSimulation> createSimulation)
    {
        Exception? failure = null;
        try
        {
            using var game = new StrideArenaGame(options, Dispatcher, createSimulation, stop.Token, () => ready.TrySetResult());
            game.Run(options.Headless ? new Stride.Games.GameContextHeadless(320, 180) : null);
            Metrics = game.Metrics;
        }
        catch (Exception exception) { failure = exception; ready.TrySetException(exception); }
        finally
        {
            Dispatcher.Complete(failure);
            if (failure is null) { ready.TrySetCanceled(); finished.TrySetResult(); }
            else finished.TrySetException(failure);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) { await finished.Task; return; }
        stop.Cancel();
        try { await finished.Task; }
        finally { Dispatcher.Dispose(); stop.Dispose(); }
    }
}
