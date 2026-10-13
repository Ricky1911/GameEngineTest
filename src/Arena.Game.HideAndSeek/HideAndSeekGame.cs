using System.Numerics;
using Arena.Engine;

namespace Arena.Game.HideAndSeek;

public sealed record HideAndSeekOptions(int RoundSeconds = 60, int HidingSeconds = 3);

public sealed class HideAndSeekGame : IGameModule
{
    private static readonly Vector2 ArenaSize = new(24, 18);
    private static readonly Vector3 ActorSize = new(0.8f, 1.2f, 0.8f);
    private readonly HideAndSeekOptions options;
    private readonly SceneEntity[] scenery;
    private readonly SceneEntity[] obstacles;
    private readonly List<GameEvent> events = [];
    private SceneEntity seeker = null!;
    private SceneEntity hider = null!;
    private long currentFrame;
    private int roundTicks;
    private int hidingTicks;
    private string winner = "";

    public HideAndSeekGame(HideAndSeekOptions? options = null)
    {
        this.options = options ?? new();
        if (this.options.RoundSeconds is < 1 or > 3600 || this.options.HidingSeconds < 0
            || this.options.HidingSeconds >= this.options.RoundSeconds)
            throw new ArgumentException("Round duration must be 1..3600 seconds, with a shorter hiding phase.");
        scenery = CreateScenery();
        obstacles = scenery.Where(e => e.Solid).ToArray();
    }

    public string Id => "hide-and-seek";
    public IReadOnlyList<AgentDefinition> Agents { get; } = Array.AsReadOnly(new[]
    {
        new AgentDefinition(1, "Seeker", "seekers"),
        new AgentDefinition(2, "Hider", "hiders")
    });
    public bool IsFinished => winner.Length > 0;

    public void Reset(int seed, int tickRate)
    {
        var random = new Random(seed);
        seeker = new SceneEntity(1, "agent", 1, "seekers", new(-9.5f, 0.6f, random.Next(-2, 3) + 0.5f),
            ActorSize, new Rgba(255, 112, 100));
        hider = new SceneEntity(2, "agent", 2, "hiders", new(9.5f, 0.6f, random.Next(-2, 3) + 0.5f),
            ActorSize, new Rgba(57, 216, 193));
        currentFrame = 0;
        roundTicks = options.RoundSeconds * tickRate;
        hidingTicks = options.HidingSeconds * tickRate;
        winner = "";
        events.Clear();
    }

    public void Tick(FrameContext frame, IReadOnlyDictionary<int, AgentAction> actions)
    {
        if (IsFinished) return;
        events.Clear();
        currentFrame = frame.Frame;
        hider = Move(hider, actions.GetValueOrDefault(2), 3.5f, frame.DeltaSeconds);
        if (frame.Frame > hidingTicks)
            seeker = Move(seeker, actions.GetValueOrDefault(1), 4.4f, frame.DeltaSeconds);

        if (frame.Frame == hidingTicks + 1)
            events.Add(new("search-started", "The seeker is now moving."));
        if (frame.Frame > hidingTicks && Vector3.Distance(seeker.Position, hider.Position) <= 1.15f && HasLineOfSight(seeker, hider))
        {
            winner = "seekers";
            events.Add(new("caught", "The seeker caught the hider."));
        }
        else if (frame.Frame >= roundTicks)
        {
            winner = "hiders";
            events.Add(new("timeout", "The hider survived the round."));
        }
    }

    public GameView Capture(int observerId)
    {
        var entities = new List<SceneEntity>(scenery);
        var own = observerId == 1 ? seeker : hider;
        var other = observerId == 1 ? hider : seeker;
        if (observerId == 0)
        {
            entities.Add(seeker);
            entities.Add(hider);
        }
        else
        {
            entities.Add(own);
            // The seeker cannot see the hider during the hiding phase. Sight is 360 degrees.
            var canLook = observerId != 1 || currentFrame >= hidingTicks;
            if (canLook && Vector3.Distance(own.Position, other.Position) <= 9f && HasLineOfSight(own, other))
                entities.Add(other);
        }
        var phase = IsFinished ? "finished" : currentFrame < hidingTicks ? "hiding" : "seeking";
        return new GameView(phase, IsFinished, winner, (int)Math.Max(0, roundTicks - currentFrame),
            entities.AsReadOnly(), Array.AsReadOnly(events.ToArray()));
    }

    private SceneEntity Move(SceneEntity actor, AgentAction action, float speed, float dt)
    {
        var direction = new Vector3(action.Move.X, 0, action.Move.Z);
        if (direction.LengthSquared() > 1) direction = Vector3.Normalize(direction);
        return actor with { Position = Geometry.MoveOnGround(actor.Position, actor.Size, direction * speed * dt, ArenaSize, obstacles) };
    }

    private bool HasLineOfSight(SceneEntity a, SceneEntity b) =>
        !obstacles.Any(box => Geometry.SegmentIntersectsBox(a.Position + Vector3.UnitY * 0.3f, b.Position + Vector3.UnitY * 0.3f, box));

    private static SceneEntity[] CreateScenery()
    {
        var wall = new Rgba(76, 100, 130);
        var low = new Rgba(111, 135, 151);
        return
        [
            new(100, "floor", 0, "", new(0, -0.15f, 0), new(24, 0.3f, 18), new Rgba(29, 43, 60)),
            new(101, "wall", 0, "", new(-4, 1, -3), new(2, 2, 6), wall, true),
            new(102, "wall", 0, "", new(4, 1, 3), new(2, 2, 6), wall, true),
            new(103, "wall", 0, "", new(0, 1, -6), new(5, 2, 2), wall, true),
            new(104, "wall", 0, "", new(0, 1, 6), new(5, 2, 2), wall, true),
            new(105, "wall", 0, "", new(0, 1, 0), new(2, 2, 3), wall, true),
            new(106, "crate", 0, "", new(-7.5f, 0.65f, 4), new(2, 1.3f, 2), low, true),
            new(107, "crate", 0, "", new(7.5f, 0.65f, -4), new(2, 1.3f, 2), low, true)
        ];
    }
}
