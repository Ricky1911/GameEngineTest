using Arena.Demo;
using Arena.Protocol;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Arena.Tests;

public sealed class GrpcTests
{
    [Fact]
    public async Task RealHttp2TransportEnforcesIdentityVisibilityAndFrameSemantics()
    {
        var tokens = new AccessTokens();
        await using var app = DemoHost.Build(new DemoOptions { Port = 0, Headless = true, Manual = true, Bots = false }, tokens);
        await app.StartAsync();
        try
        {
            using var channel = GrpcChannel.ForAddress(Assert.Single(app.Urls));
            var agent = new AgentGateway.AgentGatewayClient(channel);
            var referee = new RefereeGateway.RefereeGatewayClient(channel);
            var observation = await agent.ObserveAsync(new ObserveRequest { Token = tokens.Seeker });
            Assert.Equal(320, observation.Width);
            Assert.Equal(180, observation.Height);
            Assert.Equal(320 * 180 * 3, observation.Rgb24.Length);
            Assert.All(observation.Rgb24.ToByteArray(), b => Assert.Equal(0, b));
            var hiderImage = await agent.ObserveAsync(new ObserveRequest { Token = tokens.Hider });
            var engineImage = await app.Services.GetRequiredService<Engine.SimulationDispatcher>().InvokeAsync(s => s.Observe(2));
            Assert.Equal(engineImage.Image.Pixels.ToArray(), hiderImage.Rgb24.ToByteArray());
            Assert.Contains(hiderImage.Rgb24.ToByteArray(), b => b != 0);
            // Schema whitelist prevents accidental reintroduction of structured game-state fields.
            Assert.Equal(new[] { "episode", "frame", "width", "height", "rgb24", "finished" },
                ImageObservation.Descriptor.Fields.InFieldNumberOrder().Select(f => f.Name).ToArray());
            var denied = await Assert.ThrowsAsync<RpcException>(async () =>
                await referee.InspectAsync(new RefereeRequest { Token = tokens.Seeker }));
            Assert.Equal(StatusCode.Unauthenticated, denied.StatusCode);
            var invalid = await Assert.ThrowsAsync<RpcException>(async () =>
                await agent.ObserveAsync(new ObserveRequest { Token = "invalid" }));
            Assert.Equal(StatusCode.Unauthenticated, invalid.StatusCode);
            var all = await referee.InspectAsync(new RefereeRequest { Token = tokens.Referee });
            Assert.Equal(42, all.Seed);
            Assert.Equal(2, all.Entities.Count(e => e.AgentId != 0));
            await Task.Delay(100);
            var idle = await referee.InspectAsync(new RefereeRequest { Token = tokens.Referee });
            Assert.Equal(all, idle); // Wall time and rendering must not move a manually stepped world.
            var before = all.Entities.Single(e => e.AgentId == 2).Position.X;
            var action = new ActionRequest
            {
                Token = tokens.Hider, Episode = all.Episode, TargetFrame = 1, Move = new Protocol.Vector3 { Z = 1 }
            };
            await agent.SubmitActionAsync(action);
            var duplicate = await Assert.ThrowsAsync<RpcException>(async () => await agent.SubmitActionAsync(action));
            Assert.Equal(StatusCode.AlreadyExists, duplicate.StatusCode);
            var step = new StepRequest { Token = tokens.Referee, Episode = all.Episode, ExpectedFrame = 0, Count = 1 };
            var advanced = await referee.StepAsync(step);
            Assert.Equal(1, advanced.Frame);
            Assert.True(advanced.Entities.Single(e => e.AgentId == 2).Position.X < before);
            var repeatedStep = await Assert.ThrowsAsync<RpcException>(async () => await referee.StepAsync(step));
            Assert.Equal(StatusCode.FailedPrecondition, repeatedStep.StatusCode);
            var reset = await referee.ResetAsync(new ResetRequest { Token = tokens.Referee, Seed = 42 });
            Assert.Equal(0, reset.Frame);
            Assert.NotEqual(all.Episode, reset.Episode);
            var oldAction = await Assert.ThrowsAsync<RpcException>(async () => await agent.SubmitActionAsync(action));
            Assert.Equal(StatusCode.FailedPrecondition, oldAction.StatusCode);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task RealtimeServerRejectsManualStepping()
    {
        var tokens = new AccessTokens();
        await using var app = DemoHost.Build(new DemoOptions { Port = 0, Headless = true, Bots = false }, tokens);
        await app.StartAsync();
        try
        {
            using var channel = GrpcChannel.ForAddress(Assert.Single(app.Urls));
            var referee = new RefereeGateway.RefereeGatewayClient(channel);
            var error = await Assert.ThrowsAsync<RpcException>(async () => await referee.StepAsync(new StepRequest
            {
                Token = tokens.Referee, Episode = 1, Count = 1
            }));
            Assert.Equal(StatusCode.FailedPrecondition, error.StatusCode);
        }
        finally { await app.StopAsync(); }
    }
}
