using System.Diagnostics;
using Arena.Engine;

namespace Arena.Demo;

public sealed class SimulationRuntime(GameSimulation simulation, DemoOptions options) : BackgroundService
{
    private int paused;
    public bool IsPaused => Volatile.Read(ref paused) != 0;
    public bool IsManual => options.Manual;
    public void TogglePause() => Interlocked.Exchange(ref paused, IsPaused ? 0 : 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Manual) return;
        var clock = Stopwatch.StartNew();
        var previous = clock.Elapsed.TotalSeconds;
        var accumulator = 0d;
        var step = 1d / simulation.TickRate;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(4));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = clock.Elapsed.TotalSeconds;
            var elapsed = now - previous;
            previous = now;
            if (IsPaused) { accumulator = 0; continue; }
            // A slow renderer never changes simulation dt. Limit wall-time backlog after stalls.
            accumulator += Math.Min(elapsed, 0.25);
            while (accumulator >= step)
            {
                simulation.Tick();
                accumulator -= step;
            }
        }
    }
}
