using System.Numerics;

namespace Arena.Engine;

public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255);
public enum PrimitiveShape { Box, Sphere }
public sealed record AgentDefinition(int Id, string Name, string Team);

public sealed record SceneEntity(
    int Id, string Kind, int AgentId, string Team, Vector3 Position, Vector3 Size,
    Rgba Color, bool Solid = false, PrimitiveShape Shape = PrimitiveShape.Box);

public sealed record GameEvent(string Kind, string Message);

public sealed record GameView(
    string Phase, bool Finished, string Winner, int RemainingTicks,
    IReadOnlyList<SceneEntity> Entities, IReadOnlyList<GameEvent> Events);

public sealed record WorldSnapshot(
    long Episode, long Frame, int TickRate, int Seed, string GameId, int ObserverId, GameView View);

// Movement is normalized by the engine. Games decide which axes/abilities are meaningful.
public readonly record struct AgentAction(Vector3 Move, string Ability = "")
{
    public static AgentAction Idle => new(Vector3.Zero);
}

public readonly record struct FrameContext(long Frame, int TickRate)
{
    public float DeltaSeconds => 1f / TickRate;
}

// A module owns its rules and mutable world. Only GameSimulation calls it after construction.
// Capture must return an immutable snapshot and filter private information for observerId != 0.
public interface IGameModule
{
    string Id { get; }
    IReadOnlyList<AgentDefinition> Agents { get; }
    bool IsFinished { get; }
    void Reset(int seed, int tickRate);
    void Tick(FrameContext frame, IReadOnlyDictionary<int, AgentAction> actions);
    GameView Capture(int observerId);
}

public interface IAgentController
{
    AgentAction Decide(WorldSnapshot observation);
}

public enum SimulationError { InvalidArgument, FailedPrecondition, AlreadyExists, NotFound }

public sealed class SimulationException(SimulationError error, string message) : Exception(message)
{
    public SimulationError Error { get; } = error;
}
