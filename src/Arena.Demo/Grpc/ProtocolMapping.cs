using Arena.Engine;
using Arena.Protocol;
using Google.Protobuf;
using Grpc.Core;

namespace Arena.Demo.Grpc;

internal static class ProtocolMapping
{
    public static Snapshot Map(WorldSnapshot source)
    {
        var result = new Snapshot
        {
            Episode = source.Episode, Frame = source.Frame, TickRate = source.TickRate,
            GameId = source.GameId, Seed = source.Seed, Phase = source.View.Phase,
            Finished = source.View.Finished, Winner = source.View.Winner, RemainingTicks = source.View.RemainingTicks
        };
        result.Entities.AddRange(source.View.Entities.Select(e => new Entity
        {
            Id = e.Id, Kind = e.Kind, AgentId = e.AgentId, Team = e.Team, Solid = e.Solid,
            Position = Map(e.Position), Size = Map(e.Size), Shape = (Shape)e.Shape, YawRadians = e.YawRadians,
            Color = new Color { R = e.Color.R, G = e.Color.G, B = e.Color.B, A = e.Color.A }
        }));
        result.Events.AddRange(source.View.Events.Select(e => new Event { Kind = e.Kind, Message = e.Message }));
        return result;
    }

    public static Protocol.ImageObservation Map(Engine.ImageObservation source) => new()
    {
        Episode = source.Episode, Frame = source.Frame, Finished = source.Finished,
        Width = source.Image.Width, Height = source.Image.Height,
        Rgb24 = ByteString.CopyFrom(source.Image.Pixels.Span)
    };

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
