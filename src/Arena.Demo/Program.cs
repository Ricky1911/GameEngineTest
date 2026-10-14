using Arena.Engine;
using Arena.Rendering;

namespace Arena.Demo;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--help"))
        {
            Console.WriteLine("Arena.Demo [--headless] [--manual] [--no-bots] [--port 50051] [--tick-rate 20] [--seed 42] [--round-seconds 60] [--view 0|1|2] [--exit-after-frames N] [--screenshot path.png]");
            return 0;
        }
        try { return Run(DemoOptions.Parse(args)); }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Arena failed: {exception.Message}");
            return 1;
        }
    }

    private static int Run(DemoOptions options)
    {
        var tokens = new AccessTokens();
        var app = DemoHost.Build(options, tokens);
        try
        {
            app.StartAsync().GetAwaiter().GetResult();
            Console.WriteLine($"Arena ready at http://127.0.0.1:{options.Port} ({(options.Manual ? "manual" : "realtime")}, {options.TickRate} Hz)");
            Console.WriteLine($"Seeker token:  {tokens.Seeker}\nHider token:   {tokens.Hider}\nReferee token: {tokens.Referee}");
            Console.WriteLine("Use --help for options. Ctrl+C or close the window to stop.");
            var simulation = app.Services.GetRequiredService<GameSimulation>();
            var runtime = app.Services.GetRequiredService<SimulationRuntime>();
            if (options.Headless)
            {
                WaitHeadless(app, simulation, options).GetAwaiter().GetResult();
            }
            else
            {
                void Step()
                {
                    if (!options.Manual) return;
                    var current = simulation.Snapshot();
                    try { simulation.Advance(current.Episode, current.Frame, 1); }
                    catch (SimulationException) { /* A concurrent referee already advanced. */ }
                }
                SceneWindow.Run(simulation.Snapshot, simulation.Observe,
                    new WindowControls(runtime.TogglePause, Step, () => simulation.Reset(simulation.Snapshot().Seed),
                        () => runtime.IsPaused, options.Manual, $"127.0.0.1:{options.Port}"),
                    app.Lifetime.ApplicationStopping, options.ExitAfterFrames, options.Screenshot, options.InitialView);
            }
            return 0;
        }
        finally
        {
            app.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static async Task WaitHeadless(WebApplication app, GameSimulation simulation, DemoOptions options)
    {
        if (options.ExitAfterFrames is null) { await app.WaitForShutdownAsync(); return; }
        try
        {
            while (!app.Lifetime.ApplicationStopping.IsCancellationRequested)
            {
                var snapshot = simulation.Snapshot();
                if (snapshot.Frame >= options.ExitAfterFrames || snapshot.View.Finished) break;
                await Task.Delay(20, app.Lifetime.ApplicationStopping);
            }
        }
        catch (OperationCanceledException) when (app.Lifetime.ApplicationStopping.IsCancellationRequested) { }
    }
}
