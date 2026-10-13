using System.Numerics;

namespace Arena.Engine;

/// <summary>A single writer, fixed-step simulation shared by rendering and RPC callers.</summary>
public sealed class GameSimulation
{
    private readonly object gate = new();
    private readonly IGameModule game;
    private readonly IReadOnlyDictionary<int, IAgentController> controllers;
    private readonly Dictionary<(long Frame, int Agent), AgentAction> pending = [];
    private long episode;
    private long frame;
    private int seed;

    public GameSimulation(IGameModule game, int tickRate = 20, int seed = 42,
        IReadOnlyDictionary<int, IAgentController>? controllers = null)
    {
        if (tickRate is < 1 or > 120)
            throw new ArgumentOutOfRangeException(nameof(tickRate), "Tick rate must be 1..120.");
        this.game = game;
        this.controllers = controllers is null
            ? new Dictionary<int, IAgentController>()
            : new Dictionary<int, IAgentController>(controllers);
        Agents = Array.AsReadOnly(game.Agents.OrderBy(a => a.Id).ToArray());
        if (Agents.Count == 0 || Agents.Any(a => a.Id <= 0) || Agents.Select(a => a.Id).Distinct().Count() != Agents.Count)
            throw new ArgumentException("A game must define unique, positive agent IDs.", nameof(game));
        if (this.controllers.Keys.Any(id => Agents.All(a => a.Id != id)))
            throw new ArgumentException("Controller refers to an unknown agent.", nameof(controllers));
        TickRate = tickRate;
        Reset(seed);
    }

    public string GameId => game.Id;
    public int TickRate { get; }
    public int MaxActionLead => TickRate * 2;
    public IReadOnlyList<AgentDefinition> Agents { get; }

    public WorldSnapshot Snapshot(int observerId = 0)
    {
        lock (gate)
        {
            if (observerId != 0) RequireAgent(observerId);
            return Capture(observerId);
        }
    }

    public WorldSnapshot Reset(int newSeed)
    {
        lock (gate)
        {
            game.Reset(newSeed, TickRate);
            seed = newSeed;
            frame = 0;
            episode++;
            pending.Clear();
            return Capture(0);
        }
    }

    public void Submit(int agentId, long expectedEpisode, long targetFrame, AgentAction action)
    {
        lock (gate)
        {
            RequireAgent(agentId);
            RequireEpisode(expectedEpisode);
            if (game.IsFinished) Fail(SimulationError.FailedPrecondition, "The round has finished; reset it first.");
            if (targetFrame <= frame || targetFrame > frame + MaxActionLead)
                Fail(SimulationError.FailedPrecondition, $"Target frame must be in [{frame + 1}, {frame + MaxActionLead}].");
            var move = action.Move;
            if (!float.IsFinite(move.X) || !float.IsFinite(move.Y) || !float.IsFinite(move.Z))
                Fail(SimulationError.InvalidArgument, "Movement must contain finite numbers.");
            if (action.Ability is null || action.Ability.Length > 64)
                Fail(SimulationError.InvalidArgument, "Ability must be a string of at most 64 characters.");
            // Double precision avoids overflow while normalizing even very large finite inputs.
            var length = Math.Sqrt((double)move.X * move.X + (double)move.Y * move.Y + (double)move.Z * move.Z);
            if (length > 1)
                move = new Vector3((float)(move.X / length), (float)(move.Y / length), (float)(move.Z / length));
            if (!pending.TryAdd((targetFrame, agentId), action with { Move = move }))
                Fail(SimulationError.AlreadyExists, "An action for this agent and frame is already queued.");
        }
    }

    // The referee provides a compare-and-advance guard, so retries cannot advance twice.
    public WorldSnapshot Advance(long expectedEpisode, long expectedFrame, int count)
    {
        lock (gate)
        {
            RequireEpisode(expectedEpisode);
            if (expectedFrame != frame)
                Fail(SimulationError.FailedPrecondition, $"Expected frame {expectedFrame}; current frame is {frame}.");
            if (count is < 1 or > 600)
                Fail(SimulationError.InvalidArgument, "Step count must be 1..600.");
            for (var i = 0; i < count && !game.IsFinished; i++) TickCore();
            return Capture(0);
        }
    }

    public void Tick()
    {
        lock (gate)
        {
            if (!game.IsFinished) TickCore();
        }
    }

    private void TickCore()
    {
        var nextFrame = frame + 1;
        var actions = new Dictionary<int, AgentAction>();
        // Every controller observes the same completed frame before any world mutation.
        foreach (var agent in Agents)
        {
            actions[agent.Id] = pending.Remove((nextFrame, agent.Id), out var action) ? action
                : controllers.TryGetValue(agent.Id, out var controller) ? controller.Decide(Capture(agent.Id))
                : AgentAction.Idle;
        }
        game.Tick(new FrameContext(nextFrame, TickRate), actions);
        frame = nextFrame;
        if (game.IsFinished) pending.Clear();
    }

    private WorldSnapshot Capture(int observerId) =>
        new(episode, frame, TickRate, observerId == 0 ? seed : 0, game.Id, observerId, game.Capture(observerId));

    private void RequireAgent(int id)
    {
        if (Agents.All(a => a.Id != id)) Fail(SimulationError.NotFound, $"Unknown agent {id}.");
    }

    private void RequireEpisode(long expected)
    {
        if (expected != episode) Fail(SimulationError.FailedPrecondition, "Episode changed; fetch a new observation.");
    }

    private static void Fail(SimulationError code, string message) => throw new SimulationException(code, message);
}
