using System.Numerics;
using Arena.Engine;
using Arena.Game.HideAndSeek;

namespace Arena.Tests;

[Collection("Stride")]
public sealed class PhysicsAndVisionTests(StrideFixture fixture)
{
    [Fact]
    public Task BepuCapsuleStopsAtWallAndFallsOntoFloor() => fixture.Run((physics, _) =>
    {
        SceneEntity floor = new(100, "floor", 0, "", new(0, -0.25f, 0), new(20, 0.5f, 20), new Rgba(), true);
        SceneEntity wall = new(101, "wall", 0, "", new(0, 1, 0), new(1, 2, 4), new Rgba(), true);
        SceneEntity actor = new(1, "agent", 1, "", new(-4, 3, 0), new(0.8f, 1.2f, 0.8f), new Rgba(), Shape: PrimitiveShape.Capsule);
        physics.Reset([floor, wall], [actor], 20);
        for (var i = 0; i < 80; i++) { physics.Drive(1, new(4, 0, 0)); physics.Advance(); }
        Assert.InRange(physics.Position(1).X, -1.01f, -0.85f);
        Assert.InRange(physics.Position(1).Y, 0.58f, 0.62f);
        Assert.True(physics.IsGrounded(1));
        Assert.True(physics.IsOccluded(new(-4, 1, 0), new(4, 1, 0)));
        Assert.False(physics.IsOccluded(new(-4, 3, 0), new(4, 3, 0)));
    });

    [Fact]
    public Task JumpAndDecisionStepUseBepuSubsteps() => fixture.Run((physics, renderer) =>
    {
        var simulation = new GameSimulation(new HideAndSeekGame(physics, new(60, 0)), renderer: renderer);
        var initial = simulation.Snapshot();
        simulation.Advance(initial.Episode, 0, 10);
        Assert.True(physics.IsGrounded(2));
        var before = physics.Position(2);
        var substeps = physics.CompletedSubsteps;
        simulation.Submit(2, initial.Episode, 11, new(Vector3.Zero, "jump"));
        simulation.Tick();
        Assert.True(physics.Position(2).Y > before.Y + 0.1f);
        Assert.Equal(3, physics.CompletedSubsteps - substeps);
        simulation.Advance(initial.Episode, 11, 40);
        Assert.InRange(physics.Position(2).Y, 0.58f, 0.62f);
    });

    [Fact]
    public Task HidingPhaseProducesBlackPixelsAndEndsAtExactFrame() => fixture.Run((physics, renderer) =>
    {
        var simulation = new GameSimulation(new HideAndSeekGame(physics), renderer: renderer);
        var initial = simulation.Observe(1);
        Assert.All(initial.Image.Pixels.ToArray(), b => Assert.Equal(0, b));
        Assert.Contains(simulation.Observe(2).Image.Pixels.ToArray(), b => b != 0);
        simulation.Advance(initial.Episode, 0, 59);
        Assert.All(simulation.Observe(1).Image.Pixels.ToArray(), b => Assert.Equal(0, b));
        simulation.Tick();
        Assert.Contains(simulation.Observe(1).Image.Pixels.ToArray(), b => b != 0);
    });

    [Fact]
    public Task GpuDepthOccludesHiddenAndBehindCameraObjects() => fixture.Run((_, renderer) =>
    {
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
    });

    [Fact]
    public Task TurningChangesImageAndMovementIsRelativeToHeading() => fixture.Run((physics, renderer) =>
    {
        var simulation = new GameSimulation(new HideAndSeekGame(physics, new(60, 0)), renderer: renderer);
        var first = simulation.Observe(2);
        Assert.Same(first, simulation.Observe(2));
        var start = simulation.Snapshot().View.Entities.Single(e => e.AgentId == 2);
        for (var frame = 1; frame <= 10; frame++)
        {
            simulation.Submit(2, first.Episode, frame, new(Vector3.Zero, Turn: 1));
            simulation.Tick();
        }
        var turned = simulation.Snapshot().View.Entities.Single(e => e.AgentId == 2);
        Assert.Equal(start.Position.X, turned.Position.X, 4);
        Assert.Equal(start.Position.Z, turned.Position.Z, 4);
        Assert.Equal(0, turned.YawRadians, 4);
        Assert.False(first.Image.Pixels.Span.SequenceEqual(simulation.Observe(2).Image.Pixels.Span));
        simulation.Submit(2, first.Episode, 11, new(Vector3.UnitZ));
        simulation.Tick();
        var moved = simulation.Snapshot().View.Entities.Single(e => e.AgentId == 2);
        Assert.True(moved.Position.Z < turned.Position.Z);
        Assert.Equal(moved.Position.X, turned.Position.X, 4);
        simulation.Reset(42);
        Assert.Equal(first.Image.Pixels.ToArray(), simulation.Observe(2).Image.Pixels.ToArray());
    });

    [Fact]
    public Task BuiltInControllerReceivesTheSameImageAndObservationDoesNotAdvancePhysics() => fixture.Run((physics, renderer) =>
    {
        var controller = new RecordingController();
        var simulation = new GameSimulation(new HideAndSeekGame(physics), controllers: new Dictionary<int, IAgentController> { [2] = controller }, renderer: renderer);
        var steps = physics.CompletedSubsteps;
        var position = physics.Position(2);
        var observed = simulation.Observe(2);
        for (var i = 0; i < 5; i++) simulation.Observe(1);
        Assert.Equal(steps, physics.CompletedSubsteps);
        Assert.Equal(position, physics.Position(2));
        simulation.Tick();
        Assert.Same(observed, controller.Received);
    });

    private static bool HasRedPixel(byte[] pixels)
    {
        for (var i = 0; i < pixels.Length; i += 3)
            if (pixels[i] > 150 && pixels[i + 1] < 20 && pixels[i + 2] < 20) return true;
        return false;
    }

    private sealed class RecordingController : IAgentController
    {
        public ImageObservation? Received { get; private set; }
        public AgentAction Decide(ImageObservation observation) { Received = observation; return AgentAction.Idle; }
    }
}
