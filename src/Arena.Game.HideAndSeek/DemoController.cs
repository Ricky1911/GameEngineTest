using System.Numerics;
using Arena.Engine;

namespace Arena.Game.HideAndSeek;

/// <summary>A deliberately small vision baseline: color tracking and image-space wall avoidance.</summary>
/// <param name="seeker">The assigned role is match configuration, not an observation of the world.</param>
public sealed class DemoController(bool seeker) : IAgentController
{
    public AgentAction Decide(ImageObservation observation)
    {
        if (observation.Finished) return AgentAction.Idle;
        var image = observation.Image;
        var pixels = image.Pixels.Span;
        long targetX = 0;
        var targets = 0;
        var lit = false;
        var walls = 0;
        var samples = 0;
        for (var y = 0; y < image.Height; y += 2)
        {
            for (var x = 0; x < image.Width; x += 2)
            {
                var index = (y * image.Width + x) * 3;
                var r = pixels[index];
                var g = pixels[index + 1];
                var b = pixels[index + 2];
                lit |= r != 0 || g != 0 || b != 0;
                var target = seeker ? g > 90 && g > r * 1.6f && g > b * 1.02f
                    : r > 100 && r > g * 1.5f && r > b * 1.5f;
                if (target) { targets++; targetX += x; }
                if (x > image.Width * 0.4f && x < image.Width * 0.6f && y > image.Height * 0.65f && y < image.Height * 0.85f)
                {
                    samples++;
                    if (b > 82 && g > 60 && b > r && !target) walls++;
                }
            }
        }
        if (!lit) return AgentAction.Idle;
        if (targets >= 3)
        {
            var error = ((float)targetX / targets / image.Width - 0.5f) * 2;
            if (!seeker) return new AgentAction(new Vector3(0, 0, 0.8f), Turn: error >= 0 ? -1 : 1);
            return new AgentAction(new Vector3(0, 0, 0.85f), Turn: Math.Clamp(error * 1.8f, -1, 1));
        }
        // No map, pathfinding, positions or hidden opponent state is available here.
        if (samples > 0 && walls > samples * 0.5f)
            return new AgentAction(new Vector3(0, 0, 0.1f), Turn: 1);
        return new AgentAction(new Vector3(0, 0, 0.85f), Turn: observation.Frame % 180 < 30 ? 0.3f : 0);
    }
}
