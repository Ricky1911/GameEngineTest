using System.Net;
using Arena.Demo.Grpc;
using Arena.Engine;
using Arena.Game.HideAndSeek;
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
            [1] = new DemoController(), [2] = new DemoController()
        } : null;
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(tokens ?? new AccessTokens());
        builder.Services.AddSingleton(new GameSimulation(new HideAndSeekGame(new(options.RoundSeconds)), options.TickRate, options.Seed, controllers));
        builder.Services.AddSingleton<SimulationRuntime>();
        builder.Services.AddHostedService(provider => provider.GetRequiredService<SimulationRuntime>());
        var app = builder.Build();
        app.MapGrpcService<AgentService>();
        app.MapGrpcService<RefereeService>();
        return app;
    }
}
