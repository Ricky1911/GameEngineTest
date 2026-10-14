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
            Console.Error.WriteLine($"Arena failed: {exception}");
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
            app.WaitForShutdownAsync().GetAwaiter().GetResult();
            var metrics = app.Services.GetRequiredService<StrideHost>().Metrics;
            Console.WriteLine($"Backend: Stride 4.4 / Bepu / {metrics.Device}");
            Console.WriteLine($"GPU observations: {metrics.Images}, mean render+readback: {metrics.ImageMilliseconds / Math.Max(1, metrics.Images):0.00} ms; physics substeps: {metrics.PhysicsSteps}");
            return 0;
        }
        finally
        {
            app.StopAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

}
