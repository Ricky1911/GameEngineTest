using System.Threading.Channels;

namespace Arena.Engine;

/// <summary>Marshals RPC work onto the owning engine thread. No GPU or world access on RPC threads.</summary>
public sealed class SimulationDispatcher : IDisposable
{
    private readonly AutoResetEvent workAvailable = new(false);
    private readonly object signalGate = new();
    private bool disposed;
    private readonly Channel<ICommand> commands = Channel.CreateBounded<ICommand>(new BoundedChannelOptions(256)
    {
        SingleReader = true, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false
    });

    public async Task<T> InvokeAsync<T>(Func<GameSimulation, T> action, CancellationToken cancellation = default)
    {
        var command = new Command<T>(action, cancellation);
        await commands.Writer.WriteAsync(command, cancellation);
        lock (signalGate)
        {
            if (!disposed) workAvailable.Set();
        }
        return await command.Completion.Task.WaitAsync(cancellation);
    }

    public void WaitForWork(int timeoutMilliseconds) => workAvailable.WaitOne(timeoutMilliseconds);

    // Only the engine thread may call Drain. Limit the batch so rendering/input cannot starve.
    public void Drain(GameSimulation simulation, int limit = 32)
    {
        for (var i = 0; i < limit && commands.Reader.TryRead(out var command); i++) command.Execute(simulation);
    }

    public void Complete(Exception? failure = null)
    {
        failure ??= new OperationCanceledException("The engine stopped.");
        commands.Writer.TryComplete(failure);
        while (commands.Reader.TryRead(out var command)) command.Fail(failure);
    }

    public void Dispose()
    {
        lock (signalGate)
        {
            if (disposed) return;
            disposed = true;
            workAvailable.Dispose();
        }
    }

    private interface ICommand
    {
        void Execute(GameSimulation simulation);
        void Fail(Exception failure);
    }

    private sealed class Command<T>(Func<GameSimulation, T> action, CancellationToken cancellation) : ICommand
    {
        public TaskCompletionSource<T> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Execute(GameSimulation simulation)
        {
            if (cancellation.IsCancellationRequested) { Completion.TrySetCanceled(cancellation); return; }
            try { Completion.TrySetResult(action(simulation)); }
            catch (Exception exception) { Completion.TrySetException(exception); }
        }
        public void Fail(Exception failure) => Completion.TrySetException(failure);
    }
}
