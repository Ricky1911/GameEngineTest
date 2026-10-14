using System.Diagnostics;
using Arena.Engine;
using Stride.BepuPhysics;
using Stride.BepuPhysics.Definitions;
using Stride.BepuPhysics.Definitions.Colliders;
using Stride.Core.Mathematics;
using Stride.Engine;
using NVector3 = System.Numerics.Vector3;

namespace Arena.Rendering;

/// <summary>Stride's Bepu integration owns collision, gravity, support constraints and character motion.</summary>
public sealed class BepuCharacterWorld : ICharacterWorld, IDisposable
{
    private readonly Game game;
    private readonly Action<TimeSpan> stepEngine;
    private readonly BepuConfiguration configuration;
    private readonly List<Entity> entities = [];
    private readonly Dictionary<int, CharacterComponent> characters = [];
    private BepuSimulation physics;
    private int substeps;

    public BepuCharacterWorld(Game game, Action<TimeSpan> stepEngine)
    {
        this.game = game;
        this.stepEngine = stepEngine;
        configuration = (BepuConfiguration)BepuConfiguration.NewInstance(game.Services);
        game.Services.AddService(configuration);
        physics = configuration.BepuSimulations[0];
    }

    public long CompletedSubsteps { get; private set; }
    public double TotalStepMilliseconds { get; private set; }
    public int SubstepsPerDecision => substeps;

    public void Reset(IReadOnlyList<SceneEntity> scenery, IReadOnlyList<SceneEntity> actors, int tickRate)
    {
        foreach (var entity in entities) entity.Scene = null;
        entities.Clear();
        characters.Clear();
        physics.Dispose();
        substeps = (int)Math.Ceiling(60d / tickRate);
        // At 20 decision Hz this is 3 x ~1/60s. TimeSpan's rounding is < 0.1 microsecond per step.
        physics = new BepuSimulation
        {
            FixedTimeStep = TimeSpan.FromSeconds(1d / (tickRate * substeps)),
            ThreadCount = 1, Deterministic = true, ParallelUpdate = false,
            SoftStartDuration = TimeSpan.Zero, MaxStepPerFrame = 1,
            PoseGravity = new Vector3(0, -9.81f, 0)
        };
        configuration.BepuSimulations[0] = physics;
        var root = game.SceneSystem.SceneInstance.RootScene;
        foreach (var item in scenery.Where(e => e.Solid))
        {
            ColliderBase collider = item.Shape switch
            {
                PrimitiveShape.Sphere => new SphereCollider { Radius = item.Size.X / 2 },
                PrimitiveShape.Capsule => new CapsuleCollider { Radius = item.Size.X / 2, Length = item.Size.Y - item.Size.X },
                _ => new BoxCollider { Size = ToStride(item.Size) }
            };
            var entity = new Entity($"physics-{item.Id}")
            {
                new StaticComponent
                {
                    CollisionLayer = CollisionLayer.Layer0,
                    Collider = new CompoundCollider { Colliders = { collider } }
                }
            };
            entity.Transform.Position = ToStride(item.Position);
            entity.Scene = root;
            entities.Add(entity);
        }
        foreach (var item in actors)
        {
            var character = new CharacterComponent
            {
                CollisionLayer = CollisionLayer.Layer1,
                Collider = new CompoundCollider
                {
                    Colliders = { new CapsuleCollider { Radius = item.Size.X / 2, Length = item.Size.Y - item.Size.X, Mass = 1 } }
                },
                Speed = 1, MaximumHorizontalForce = 1000, MaximumVerticalForce = 1000,
                AirControlScale = 1, AirControlForceScale = 0.1f, JumpForce = 4,
                InterpolationMode = InterpolationMode.None
            };
            var entity = new Entity($"physics-character-{item.Id}") { character };
            entity.Transform.Position = ToStride(item.Position);
            entity.Scene = root;
            entities.Add(entity);
            characters.Add(item.Id, character);
        }
    }

    public void Drive(int entityId, NVector3 worldVelocity, bool jump = false)
    {
        var body = characters[entityId];
        var speed = new NVector3(worldVelocity.X, 0, worldVelocity.Z).Length();
        body.Speed = MathF.Max(1, speed);
        // Bodies stay upright at identity orientation; rules convert local input into world velocity.
        body.MoveVector = new Vector2(worldVelocity.X, worldVelocity.Z) / body.Speed;
        if (jump) body.TryJump();
    }

    public void Advance()
    {
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < substeps; i++) stepEngine(physics.FixedTimeStep);
        CompletedSubsteps += substeps;
        TotalStepMilliseconds += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    public NVector3 Position(int entityId) => ToNumerics(characters[entityId].Position);
    public bool IsGrounded(int entityId) => characters[entityId].IsGrounded;

    public bool IsOccluded(NVector3 from, NVector3 to)
    {
        var delta = to - from;
        var length = delta.Length();
        return length > 0.00001f && physics.RayCast(ToStride(from), ToStride(delta / length), length,
            out _, CollisionMask.Layer0);
    }

    public void Dispose()
    {
        foreach (var entity in entities) entity.Scene = null;
        entities.Clear();
        characters.Clear();
        physics.Dispose();
        configuration.BepuSimulations.Clear();
    }

    internal static Vector3 ToStride(NVector3 vector) => new(vector.X, vector.Y, vector.Z);
    internal static NVector3 ToNumerics(Vector3 vector) => new(vector.X, vector.Y, vector.Z);
}
