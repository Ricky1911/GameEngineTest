using System.Numerics;
using Arena.Engine;

namespace Arena.Game.HideAndSeek;

/// <summary>A small baseline agent, using only its filtered observation and grid pathfinding.</summary>
public sealed class DemoController : IAgentController
{
    private static readonly (int X, int Z)[] Neighbors = [(1, 0), (0, 1), (-1, 0), (0, -1)];
    private static readonly Vector3[] Patrol = [new(9.5f, 0, 6.5f), new(-9.5f, 0, 6.5f), new(-9.5f, 0, -6.5f), new(9.5f, 0, -6.5f)];

    public AgentAction Decide(WorldSnapshot observation)
    {
        var own = observation.View.Entities.Single(e => e.AgentId == observation.ObserverId);
        var enemy = observation.View.Entities.FirstOrDefault(e => e.AgentId != 0 && e.AgentId != own.AgentId);
        var floor = observation.View.Entities.Single(e => e.Kind == "floor");
        var obstacles = observation.View.Entities.Where(e => e.Solid).ToArray();
        var isSeeker = own.Team == "seekers";
        var patrolIndex = (int)(observation.Frame / (observation.TickRate * 7)) + (isSeeker ? 0 : 2);
        var target = Patrol[patrolIndex % Patrol.Length];
        if (enemy is not null)
            target = isSeeker ? enemy.Position
                : Patrol.MaxBy(p => Vector3.DistanceSquared(p, enemy.Position));

        var waypoint = FindWaypoint(own, target, floor.Size, obstacles);
        var delta = waypoint - own.Position;
        delta.Y = 0;
        var distance = delta.Length();
        if (distance < 0.02f) return AgentAction.Idle;
        // Slow down near a waypoint so a frame cannot overshoot it.
        var speed = isSeeker ? 4.4f : 3.5f;
        return new AgentAction(delta / distance * MathF.Min(1, distance * observation.TickRate / speed));
    }

    private static Vector3 FindWaypoint(SceneEntity own, Vector3 target, Vector3 arena, SceneEntity[] obstacles)
    {
        var width = (int)arena.X;
        var depth = (int)arena.Z;
        (int X, int Z) Cell(Vector3 p) => (Math.Clamp((int)MathF.Floor(p.X + width / 2f), 0, width - 1),
            Math.Clamp((int)MathF.Floor(p.Z + depth / 2f), 0, depth - 1));
        Vector3 Center((int X, int Z) cell) => new(cell.X - width / 2f + 0.5f, own.Position.Y, cell.Z - depth / 2f + 0.5f);
        var start = Cell(own.Position);
        var goal = Cell(target);
        if (start == goal) return target;
        var previous = new Dictionary<(int, int), (int, int)> { [start] = start };
        var queue = new Queue<(int X, int Z)>();
        queue.Enqueue(start);
        while (queue.TryDequeue(out var current))
        {
            if (current == goal)
            {
                var next = current;
                while (previous[next] != start) next = previous[next];
                return Center(next);
            }
            foreach (var offset in Neighbors)
            {
                var next = (X: current.X + offset.X, Z: current.Z + offset.Z);
                if (next.X < 0 || next.X >= width || next.Z < 0 || next.Z >= depth || previous.ContainsKey(next)) continue;
                if (obstacles.Any(box => Geometry.OverlapsXZ(Center(next), own.Size, box))) continue;
                previous[next] = current;
                queue.Enqueue(next);
            }
        }
        return own.Position;
    }
}
