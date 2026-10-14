using Arena.Engine;
using Arena.Game.HideAndSeek;
using Arena.Protocol;

namespace Arena.Bot;

// This baseline reuses the pixel-only example policy. A competitor can replace Decide with their own
// implementation and depend only on Arena.Protocol (or generate a client in another language).
internal sealed class BaselineAgent(bool seeker)
{
    private readonly DemoController controller = new(seeker);

    public ActionRequest Decide(Protocol.ImageObservation observation, string token)
    {
        var image = new RgbImage(observation.Width, observation.Height, observation.Rgb24.Span);
        var action = controller.Decide(new Engine.ImageObservation(observation.Episode, observation.Frame, observation.Finished, image));
        return new ActionRequest
        {
            Token = token, Episode = observation.Episode, TargetFrame = observation.Frame + 1,
            Move = new Protocol.Vector3 { X = action.Move.X, Y = action.Move.Y, Z = action.Move.Z },
            Ability = action.Ability, Turn = action.Turn
        };
    }
}
