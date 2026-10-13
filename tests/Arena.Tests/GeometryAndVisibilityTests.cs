using System.Numerics;
using Arena.Engine;
using Arena.Game.HideAndSeek;

namespace Arena.Tests;

public sealed class GeometryAndVisibilityTests
{
    [Fact]
    public void LargeMovementDoesNotTunnelThroughWalls()
    {
        var box = new SceneEntity(100, "wall", 0, "", new(0, 1, 0), new(1, 2, 4), new Rgba(), true);
        var result = Geometry.MoveOnGround(new(-5, 0.5f, 0), Vector3.One, new(10, 0, 0), new(24, 18), [box]);
        Assert.InRange(result.X, -1.2f, -0.999f);
        Assert.False(Geometry.OverlapsXZ(result, Vector3.One, box));
    }

    [Fact]
    public void SegmentTestHandlesBlockedClearAndParallelRays()
    {
        var box = new SceneEntity(100, "wall", 0, "", Vector3.Zero, new(2), new Rgba(), true);
        Assert.True(Geometry.SegmentIntersectsBox(new(-4, 0, 0), new(4, 0, 0), box));
        Assert.False(Geometry.SegmentIntersectsBox(new(-4, 3, 0), new(4, 3, 0), box));
        Assert.True(Geometry.SegmentIntersectsBox(Vector3.Zero, Vector3.Zero, box));
        Assert.False(Geometry.SegmentIntersectsBox(new(0, 3, 0), new(0, 3, 0), box));
    }

    [Fact]
    public void SeekerCannotSeeThroughCoverEvenWithinSightRange()
    {
        var game = new HideAndSeekGame(new(60, 0));
        game.Reset(42, 20);
        var actors = game.Capture(0).Entities.Where(e => e.AgentId != 0).ToArray();
        var actions = actors.ToDictionary(e => e.AgentId,
            e => new AgentAction(new Vector3(0, 0, -e.Position.Z / 100)));
        game.Tick(new FrameContext(1, 20), actions);
        // Move both actors towards the central wall from opposite sides.
        for (var frame = 2; frame <= 100; frame++)
            game.Tick(new FrameContext(frame, 20), new Dictionary<int, AgentAction>
            {
                [1] = new(Vector3.UnitX), [2] = new(-Vector3.UnitX)
            });
        actors = game.Capture(0).Entities.Where(e => e.AgentId != 0).ToArray();
        Assert.True(Vector3.Distance(actors[0].Position, actors[1].Position) < 9);
        Assert.Single(game.Capture(1).Entities, e => e.AgentId != 0);
    }
}
