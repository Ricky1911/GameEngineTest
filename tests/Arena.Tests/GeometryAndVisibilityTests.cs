using System.Numerics;
using Arena.Engine;
using Arena.Game.HideAndSeek;
using Arena.Rendering;

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
    public void HidingPhaseProducesBlackPixelsAndEndsAtExactFrame()
    {
        var simulation = new GameSimulation(new HideAndSeekGame(), renderer: new SoftwareCameraRenderer(80, 45));
        var initial = simulation.Observe(1);
        Assert.All(initial.Image.Pixels.ToArray(), b => Assert.Equal(0, b));
        Assert.Contains(simulation.Observe(2).Image.Pixels.ToArray(), b => b != 0);
        simulation.Advance(initial.Episode, 0, 59);
        Assert.All(simulation.Observe(1).Image.Pixels.ToArray(), b => Assert.Equal(0, b));
        simulation.Tick();
        Assert.Contains(simulation.Observe(1).Image.Pixels.ToArray(), b => b != 0);
    }

    [Fact]
    public void WallsFullyOccludePixelsAndObjectsBehindCameraAreInvisible()
    {
        var renderer = new SoftwareCameraRenderer(81, 45);
        var wall = new SceneEntity(10, "wall", 0, "", new(0, 1, -3), new(4, 3, 0.5f), new Rgba(90, 100, 120), true);
        var enemy = new SceneEntity(2, "agent", 2, "", new(0, 1, -5), Vector3.One, new Rgba(255, 0, 0));
        var scene = new CameraScene(new(0, 1, 0), -Vector3.UnitZ, Vector3.UnitY, 60, 40, [wall]);
        var baseline = renderer.Render(scene).Pixels.ToArray();
        var hidden = renderer.Render(scene with { Entities = [wall, enemy] }).Pixels.ToArray();
        Assert.Equal(baseline, hidden);
        var visible = renderer.Render(scene with { Entities = [enemy] }).Pixels.ToArray();
        Assert.True(HasRedPixel(visible));
        Assert.False(HasRedPixel(hidden));
        var behind = renderer.Render(scene with { Entities = [enemy with { Position = new(0, 1, 5) }] }).Pixels.ToArray();
        Assert.False(HasRedPixel(behind));
    }

    [Fact]
    public void TurningChangesImageAndMovementIsRelativeToHeading()
    {
        var simulation = new GameSimulation(new HideAndSeekGame(new(60, 0)), renderer: new SoftwareCameraRenderer(80, 45));
        var first = simulation.Observe(2);
        Assert.Same(first, simulation.Observe(2));
        var start = simulation.Snapshot().View.Entities.Single(e => e.AgentId == 2);
        for (var frame = 1; frame <= 10; frame++)
        {
            simulation.Submit(2, first.Episode, frame, new(Vector3.Zero, Turn: 1));
            simulation.Tick();
        }
        var turned = simulation.Snapshot().View.Entities.Single(e => e.AgentId == 2);
        Assert.Equal(start.Position, turned.Position);
        Assert.Equal(0, turned.YawRadians, 4);
        Assert.False(first.Image.Pixels.Span.SequenceEqual(simulation.Observe(2).Image.Pixels.Span));
        simulation.Submit(2, first.Episode, 11, new(Vector3.UnitZ));
        simulation.Tick();
        var moved = simulation.Snapshot().View.Entities.Single(e => e.AgentId == 2);
        Assert.True(moved.Position.Z < turned.Position.Z);
        Assert.Equal(moved.Position.X, turned.Position.X, 4);
        simulation.Reset(42);
        Assert.Equal(first.Image.Pixels.ToArray(), simulation.Observe(2).Image.Pixels.ToArray());
    }

    [Fact]
    public void BuiltInControllerReceivesTheSameImageAsExternalObservation()
    {
        var controller = new RecordingController();
        var simulation = new GameSimulation(new HideAndSeekGame(), controllers: new Dictionary<int, IAgentController> { [2] = controller },
            renderer: new SoftwareCameraRenderer(80, 45));
        var observed = simulation.Observe(2);
        simulation.Tick();
        Assert.Same(observed, controller.Received);
    }

    private static bool HasRedPixel(byte[] pixels)
    {
        for (var i = 0; i < pixels.Length; i += 3)
            if (pixels[i] > 150 && pixels[i + 1] < 20 && pixels[i + 2] < 20) return true;
        return false;
    }

    private sealed class RecordingController : IAgentController
    {
        public ImageObservation? Received { get; private set; }
        public AgentAction Decide(ImageObservation observation)
        {
            Received = observation;
            return AgentAction.Idle;
        }
    }
}
