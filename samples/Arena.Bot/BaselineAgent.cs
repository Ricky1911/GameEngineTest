using Arena.Engine;
using Arena.Game.HideAndSeek;
using Arena.Protocol;

namespace Arena.Bot;

// This baseline reuses the example policy. A competitor can replace Decide with their own
// implementation and depend only on Arena.Protocol (or generate a client in another language).
internal sealed class BaselineAgent
{
    private readonly DemoController controller = new();

    public ActionRequest Decide(Snapshot observation, string token)
    {
        var entities = observation.Entities.Select(e => new SceneEntity(
            e.Id, e.Kind, e.AgentId, e.Team, new(e.Position.X, e.Position.Y, e.Position.Z),
            new(e.Size.X, e.Size.Y, e.Size.Z), new Rgba((byte)e.Color.R, (byte)e.Color.G, (byte)e.Color.B, (byte)e.Color.A),
            e.Solid, (PrimitiveShape)e.Shape)).ToArray();
        var view = new GameView(observation.Phase, observation.Finished, observation.Winner,
            observation.RemainingTicks, entities, []);
        var action = controller.Decide(new WorldSnapshot(observation.Episode, observation.Frame,
            observation.TickRate, 0, observation.GameId, observation.ObserverId, view));
        return new ActionRequest
        {
            Token = token, Episode = observation.Episode, TargetFrame = observation.Frame + 1,
            Move = new Protocol.Vector3 { X = action.Move.X, Y = action.Move.Y, Z = action.Move.Z }, Ability = action.Ability
        };
    }
}
