using System.Net;
using Arena.Demo.Grpc;
using Arena.Engine;
using Arena.Game.HideAndSeek;
using Arena.Rendering;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Arena.Demo;

public static class DemoHost
{
    public static WebApplication Build(DemoOptions options, AccessTokens? tokens = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.ConfigureKestrel(server =>
            server.Listen(IPAddress.Loopback, options.Port, endpoint => endpoint.Protocols = HttpProtocols.Http2));
        builder.Services.AddGrpc(config => config.MaxReceiveMessageSize = 64 * 1024);
        var controllers = options.Bots ? new Dictionary<int, IAgentController>
        {
            [1] = new DemoController(seeker: true), [2] = new DemoController(seeker: false)
        } : null;
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(tokens ?? new AccessTokens());
        builder.Services.AddSingleton(_ => new StrideHost(
            new StrideRunOptions(options.Headless, options.Manual, options.InitialView, options.ExitAfterFrames,
                options.Screenshot, $"127.0.0.1:{options.Port}"),
            (physics, renderer) => new GameSimulation(new HideAndSeekGame(physics, new(options.RoundSeconds)),
                options.TickRate, options.Seed, controllers, renderer)));
        builder.Services.AddSingleton(provider => provider.GetRequiredService<StrideHost>().Dispatcher);
        builder.Services.AddHostedService<SimulationRuntime>();
        var app = builder.Build();
        app.MapGrpcService<AgentService>();
        app.MapGrpcService<RefereeService>();
        return app;
    }
}
