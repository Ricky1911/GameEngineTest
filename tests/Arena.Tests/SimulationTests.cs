using System.Numerics;
using Arena.Engine;
using Arena.Game.HideAndSeek;

namespace Arena.Tests;

public sealed class SimulationTests
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
    public void SameSeedAndActionsProduceSameSnapshotsAfterReset()
    {
        var simulation = new GameSimulation(new HideAndSeekGame());
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
    }

    [Fact]
    public void TimeoutEndsRoundAndStopsTheFrameCounter()
    {
        var simulation = new GameSimulation(new HideAndSeekGame(new(4, 0)), tickRate: 20);
        var state = simulation.Advance(simulation.Snapshot().Episode, 0, 200);
        Assert.True(state.View.Finished);
        Assert.Equal("hiders", state.View.Winner);
        Assert.Equal(80, state.Frame);
        simulation.Tick();
        Assert.Equal(80, simulation.Snapshot().Frame);
    }

    [Fact]
    public void CatchingEndsRoundWithSeekerWinner()
    {
        var simulation = new GameSimulation(new HideAndSeekGame(new(60, 0)), controllers:
            new Dictionary<int, IAgentController> { [1] = new DemoController() });
        for (var i = 0; i < 1200 && !simulation.Snapshot().View.Finished; i++) simulation.Tick();
        Assert.Equal("seekers", simulation.Snapshot().View.Winner);
    }

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
        public GameView Capture(int observerId) => new("moving", false, "", 0,
            [new SceneEntity(1, "agent", 1, "test", position, Vector3.One, new Rgba(255, 255, 255))], []);
    }
}
