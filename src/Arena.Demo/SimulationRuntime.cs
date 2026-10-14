using Arena.Rendering;

namespace Arena.Demo;

/// <summary>Engine startup/shutdown follows the RPC host; Stride owns simulation timing.</summary>
public sealed class SimulationRuntime(StrideHost engine, IHostApplicationLifetime lifetime) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await engine.Ready.WaitAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await engine.Completion.WaitAsync(stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { lifetime.StopApplication(); }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await engine.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}
