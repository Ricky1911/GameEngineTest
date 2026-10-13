using Arena.Engine;
using Arena.Protocol;
using Grpc.Core;

namespace Arena.Demo.Grpc;

internal static class ProtocolMapping
{
    public static Snapshot Map(WorldSnapshot source)
    {
        var result = new Snapshot
        {
            Episode = source.Episode, Frame = source.Frame, TickRate = source.TickRate,
            GameId = source.GameId, ObserverId = source.ObserverId, Phase = source.View.Phase,
            Finished = source.View.Finished, Winner = source.View.Winner, RemainingTicks = source.View.RemainingTicks
        };
        if (source.ObserverId == 0) result.Seed = source.Seed;
        result.Entities.AddRange(source.View.Entities.Select(e => new Entity
        {
            Id = e.Id, Kind = e.Kind, AgentId = e.AgentId, Team = e.Team, Solid = e.Solid,
            Position = Map(e.Position), Size = Map(e.Size), Shape = (Shape)e.Shape,
            Color = new Color { R = e.Color.R, G = e.Color.G, B = e.Color.B, A = e.Color.A }
        }));
        result.Events.AddRange(source.View.Events.Select(e => new Event { Kind = e.Kind, Message = e.Message }));
        return result;
    }

    public static Task<T> Invoke<T>(Func<T> action)
    {
        try { return Task.FromResult(action()); }
        catch (SimulationException exception)
        {
            var status = exception.Error switch
            {
                SimulationError.InvalidArgument => StatusCode.InvalidArgument,
                SimulationError.AlreadyExists => StatusCode.AlreadyExists,
                SimulationError.NotFound => StatusCode.NotFound,
                _ => StatusCode.FailedPrecondition
            };
            throw new RpcException(new Status(status, exception.Message));
        }
    }

    private static Protocol.Vector3 Map(System.Numerics.Vector3 v) => new() { X = v.X, Y = v.Y, Z = v.Z };
}
