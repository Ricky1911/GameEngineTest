using System.Numerics;
using Arena.Engine;
using Arena.Game.HideAndSeek;

namespace Arena.Tests;

[Collection("Stride")]
public sealed class SimulationTests(StrideFixture fixture)
{
    [Fact]
    public void ActionsExecuteOnlyOnTargetFrameAndDoNotPersist()
    {
        var simulation = new GameSimulation(new MovementGame());
        var initial = simulation.Snapshot();
        simulation.Submit(1, initial.Episode, 2, new AgentAction(new Vector3(100, 0, 0)));
        simulation.Tick();
        Assert.Equal(Vector3.Zero, simulation.Snapshot().View.Entities[0].Position);
        simulation.Tick();
        Assert.Equal(0.05f, simulation.Snapshot().View.Entities[0].Position.X, 5);
        simulation.Tick();
        Assert.Equal(0.05f, simulation.Snapshot().View.Entities[0].Position.X, 5);
    }

    [Fact]
    public void RejectsDuplicateStaleFutureAndNonFiniteActions()
    {
        var simulation = new GameSimulation(new MovementGame());
        var episode = simulation.Snapshot().Episode;
        simulation.Submit(1, episode, 1, AgentAction.Idle);
        Assert.Equal(SimulationError.AlreadyExists, Assert.Throws<SimulationException>(() => simulation.Submit(1, episode, 1, AgentAction.Idle)).Error);
        Assert.Equal(SimulationError.InvalidArgument, Assert.Throws<SimulationException>(() => simulation.Submit(1, episode, 2, new(new(float.NaN, 0, 0)))).Error);
        Assert.Equal(SimulationError.InvalidArgument, Assert.Throws<SimulationException>(() => simulation.Submit(1, episode, 2, new(Vector3.Zero, Turn: float.PositiveInfinity))).Error);
        Assert.Throws<SimulationException>(() => simulation.Submit(1, episode, simulation.MaxActionLead + 1, AgentAction.Idle));
        simulation.Tick();
        Assert.Throws<SimulationException>(() => simulation.Submit(1, episode, 1, AgentAction.Idle));
        var reset = simulation.Reset(123);
        Assert.NotEqual(episode, reset.Episode);
        Assert.Throws<SimulationException>(() => simulation.Submit(1, episode, 1, AgentAction.Idle));
    }

    [Fact]
    public void RefereeRetriesCannotAdvanceTwiceAndResetClearsPendingCommands()
    {
        var simulation = new GameSimulation(new MovementGame());
        var initial = simulation.Snapshot();
        simulation.Submit(1, initial.Episode, 2, new(Vector3.UnitX));
        simulation.Advance(initial.Episode, 0, 1);
        Assert.Throws<SimulationException>(() => simulation.Advance(initial.Episode, 0, 1));
        var reset = simulation.Reset(42);
        var after = simulation.Advance(reset.Episode, 0, 2);
        Assert.Equal(Vector3.Zero, after.View.Entities[0].Position);
    }

    [Fact]
    public async Task CancelledQueuedStepDoesNotMutateTheWorldOrBlockLaterCommands()
    {
        using var dispatcher = new SimulationDispatcher();
        using var stop = new CancellationTokenSource();
        var simulation = new GameSimulation(new MovementGame());
        var initial = simulation.Snapshot();
        var cancelled = dispatcher.InvokeAsync(s => s.Advance(initial.Episode, 0, 1), stop.Token);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelled);
        dispatcher.Drain(simulation);
        Assert.Equal(0, simulation.Snapshot().Frame);
        var next = dispatcher.InvokeAsync(s => s.Advance(initial.Episode, 0, 1));
        dispatcher.Drain(simulation);
        Assert.Equal(1, (await next).Frame);
        var pending = dispatcher.InvokeAsync(s => s.Snapshot());
        dispatcher.Complete();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
    }

    [Fact]
    public Task SameSeedAndActionsProduceSameSnapshotsAfterReset() => fixture.Run((physics, renderer) =>
    {
        var simulation = new GameSimulation(new HideAndSeekGame(physics), renderer: renderer);
        var first = Play();
        simulation.Reset(42);
        var second = Play();
        Assert.Equal(first, second);

        SceneEntity[] Play()
        {
            var episode = simulation.Snapshot().Episode;
            for (var i = 1; i <= 250; i++)
            {
                simulation.Submit(1, episode, i, new(new Vector3(0.3f, 0, -0.7f)));
                simulation.Submit(2, episode, i, new(new Vector3(-0.6f, 0, 0.2f)));
                simulation.Tick();
            }
            return simulation.Snapshot().View.Entities.ToArray();
        }
    });

    [Fact]
    public Task TimeoutEndsRoundAndStopsTheFrameCounter() => fixture.Run((physics, renderer) =>
    {
        var simulation = new GameSimulation(new HideAndSeekGame(physics, new(4, 0)), tickRate: 20, renderer: renderer);
        var state = simulation.Advance(simulation.Snapshot().Episode, 0, 200);
        Assert.True(state.View.Finished);
        Assert.Equal("hiders", state.View.Winner);
        Assert.Equal(80, state.Frame);
        simulation.Tick();
        Assert.Equal(80, simulation.Snapshot().Frame);
    });

    [Fact]
    public Task CatchingEndsRoundWithSeekerWinner() => fixture.Run((physics, renderer) =>
    {
        // Referee-driven route around cover, independent of the strength of any baseline agent.
        var simulation = new GameSimulation(new HideAndSeekGame(physics, new(60, 0)), renderer: renderer);
        var initial = simulation.Snapshot();
        var goal = initial.View.Entities.Single(e => e.AgentId == 2).Position;
        var waypoints = new Queue<Vector3>([new(-9.5f, 0.6f, -7.8f), new(9.5f, 0.6f, -7.8f), goal]);
        for (var i = 0; i < 1200 && !simulation.Snapshot().View.Finished; i++)
        {
            var state = simulation.Snapshot();
            var own = state.View.Entities.Single(e => e.AgentId == 1);
            var delta = waypoints.Peek() - own.Position;
            if (delta.Length() < 0.15f && waypoints.Count > 1) { waypoints.Dequeue(); delta = waypoints.Peek() - own.Position; }
            var direction = Vector3.Normalize(delta) * MathF.Min(1, delta.Length() * 20 / 4.4f);
            var forward = new Vector3(MathF.Sin(own.YawRadians), 0, -MathF.Cos(own.YawRadians));
            var right = new Vector3(MathF.Cos(own.YawRadians), 0, MathF.Sin(own.YawRadians));
            simulation.Submit(1, state.Episode, state.Frame + 1, new(new(Vector3.Dot(direction, right), 0, Vector3.Dot(direction, forward))));
            simulation.Tick();
        }
        Assert.Equal("seekers", simulation.Snapshot().View.Winner);
    });

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(60)]
    public void MovementHasTheSameSpeedAcrossTickRates(int rate)
    {
        var simulation = new GameSimulation(new MovementGame(), rate);
        var episode = simulation.Snapshot().Episode;
        for (var i = 1; i <= rate; i++)
        {
            simulation.Submit(1, episode, i, new(Vector3.UnitX));
            simulation.Tick();
        }
        Assert.Equal(1f, simulation.Snapshot().View.Entities[0].Position.X, 4);
    }

    private sealed class MovementGame : IGameModule
    {
        private Vector3 position;
        public string Id => "movement-test";
        public IReadOnlyList<AgentDefinition> Agents { get; } = [new(1, "Mover", "test")];
        public bool IsFinished => false;
        public void Reset(int seed, int tickRate) => position = Vector3.Zero;
        public void Tick(FrameContext frame, IReadOnlyDictionary<int, AgentAction> actions) => position += actions[1].Move * frame.DeltaSeconds;
        public GameView Capture() => new("moving", false, "", 0,
            [new SceneEntity(1, "agent", 1, "test", position, Vector3.One, new Rgba(255, 255, 255))], []);
        public CameraScene CaptureCamera(int agentId) => new(position, -Vector3.UnitZ, Vector3.UnitY, 60, 40, []);
    }
}
