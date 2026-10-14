using System.Diagnostics;
using Arena.Engine;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Input;
using Stride.Rendering;
using Stride.Rendering.Compositing;
using Stride.Rendering.Lights;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;
using Stride.Rendering.ProceduralModels;
using NVector3 = System.Numerics.Vector3;

namespace Arena.Rendering;

/// <summary>
/// The engine loop is explicitly driven: only AdvancePhysics supplies nonzero elapsed time.
/// RPCs, physics, render-to-texture and readback all execute on this thread, between decisions.
/// </summary>
internal sealed class StrideArenaGame : Game, IObservationRenderer
{
    private const int ImageWidth = 320;
    private const int ImageHeight = 180;
    private readonly StrideRunOptions options;
    private readonly SimulationDispatcher dispatcher;
    private readonly Func<ICharacterWorld, IObservationRenderer, GameSimulation> createSimulation;
    private readonly CancellationToken stopping;
    private readonly Action onReady;
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<int, (SceneEntity Definition, Entity Entity)> models = [];
    private readonly List<IDisposable> resources = [];
    private readonly Stopwatch wallClock = new();
    private BepuCharacterWorld world = null!;
    private GameSimulation simulation = null!;
    private CameraComponent camera = null!;
    private InputSystem inputSystem = null!;
    private Texture imageTarget = null!;
    private Texture imageDepth = null!;
    private Texture readback = null!;
    private Texture previewTexture = null!;
    private SpriteBatch sprites = null!;
    private byte[] rgba = new byte[ImageWidth * ImageHeight * 4];
    private RgbImage? lastUploaded;
    private RgbImage? rendered;
    private ImageObservation? preview;
    private WorldSnapshot? spectator;
    private CameraScene activeScene = null!;
    private bool offscreen;
    private bool warmed;
    private bool paused;
    private bool screenshotPending;
    private int view;
    private int displayedFrames;
    private double previousWallTime;
    private double accumulator;
    private float yaw = 0.67f;
    private float elevation = 0.82f;
    private float distance = 36;
    private long images;
    private double imageMilliseconds;

    public StrideArenaGame(StrideRunOptions options, SimulationDispatcher dispatcher,
        Func<ICharacterWorld, IObservationRenderer, GameSimulation> createSimulation, CancellationToken stopping, Action onReady)
    {
        this.options = options;
        this.dispatcher = dispatcher;
        this.createSimulation = createSimulation;
        this.stopping = stopping;
        this.onReady = onReady;
        view = options.InitialView;
        AutoLoadDefaultSettings = false;
        ConsoleLogLevel = LogMessageType.Warning;
        GraphicsDeviceManager.PreferredBackBufferWidth = options.Headless ? 320 : 1440;
        GraphicsDeviceManager.PreferredBackBufferHeight = options.Headless ? 180 : 900;
        GraphicsDeviceManager.PreferredGraphicsProfile = [GraphicsProfile.Level_11_0];
        GraphicsDeviceManager.SynchronizeWithVerticalRetrace = false;
        DrawWhileMinimized = true;
        TreatNotFocusedLikeMinimized = false;
    }

    public BackendMetrics Metrics => new(images, imageMilliseconds, world?.CompletedSubsteps ?? 0,
        world?.TotalStepMilliseconds ?? 0, GraphicsDevice?.Adapter.Description ?? "unknown");

    protected override Task LoadContent()
    {
        // Poll UI input once per outer loop; physics/render-only ticks must not consume key edges.
        inputSystem = GameSystems.OfType<InputSystem>().Single();
        inputSystem.Enabled = false;
        Window.Title = "ARENA / Stride + Bepu / Image-only agents";
        Window.AllowUserResizing = true;
        SceneSystem.GraphicsCompositor = GraphicsCompositorHelper.CreateDefault(false, clearColor: new Color4(0.035f, 0.055f, 0.085f, 1));
        camera = new CameraComponent
        {
            Slot = SceneSystem.GraphicsCompositor.Cameras[0].ToSlotId(), UseCustomViewMatrix = true,
            NearClipPlane = 0.04f, UseCustomAspectRatio = true
        };
        var scene = SceneSystem.SceneInstance.RootScene;
        scene.Entities.Add(new Entity("Camera") { camera });
        scene.Entities.Add(new Entity("Ambient") { new LightComponent { Type = new LightAmbient(), Intensity = 0.7f } });
        var sun = new Entity("Sun") { new LightComponent { Type = new LightDirectional(), Intensity = 0.65f } };
        sun.Transform.Rotation = Quaternion.RotationYawPitchRoll(-0.7f, -0.9f, 0);
        scene.Entities.Add(sun);
        imageTarget = Own(Texture.New2D(GraphicsDevice, ImageWidth, ImageHeight, PixelFormat.R8G8B8A8_UNorm_SRgb,
            TextureFlags.RenderTarget | TextureFlags.ShaderResource));
        imageDepth = Own(Texture.New2D(GraphicsDevice, ImageWidth, ImageHeight, PixelFormat.D24_UNorm_S8_UInt, TextureFlags.DepthStencil));
        readback = Own(imageTarget.ToStaging());
        previewTexture = Own(Texture.New2D(GraphicsDevice, ImageWidth, ImageHeight, PixelFormat.R8G8B8A8_UNorm_SRgb));
        sprites = Own(new SpriteBatch(GraphicsDevice));
        world = new BepuCharacterWorld(this, AdvancePhysics);
        simulation = createSimulation(world, this);
        return Task.CompletedTask;
    }

    protected override void RawTickProducer()
    {
        if (stopping.IsCancellationRequested) { Exit(); return; }
        if (simulation is null) { RawTick(TimeSpan.Zero); return; }
        if (!warmed)
        {
            // Compile the required shaders and validate GPU readback before accepting any RPC.
            foreach (var agent in simulation.Agents) simulation.Observe(agent.Id);
            images = 0;
            imageMilliseconds = 0; // Report steady-state work, excluding one-time shader warmup.
            warmed = true;
            wallClock.Start();
            onReady();
        }
        if (!options.Headless)
        {
            inputSystem.Update(new GameTime(wallClock.Elapsed, TimeSpan.Zero));
            HandleInput();
            if (IsExiting) return;
        }
        dispatcher.Drain(simulation);
        var now = wallClock.Elapsed.TotalSeconds;
        var elapsed = now - previousWallTime;
        previousWallTime = now;
        if (!options.Manual && !paused)
        {
            accumulator += Math.Min(elapsed, 0.25);
            var dt = 1d / simulation.TickRate;
            for (var i = 0; i < 5 && accumulator >= dt; i++)
            {
                simulation.Tick();
                accumulator -= dt;
            }
        }
        else accumulator = 0;

        if (!options.Headless)
        {
            if (view == 0)
            {
                spectator = simulation.Snapshot();
                var eye = new NVector3(MathF.Sin(yaw) * MathF.Cos(elevation), MathF.Sin(elevation), MathF.Cos(yaw) * MathF.Cos(elevation)) * distance;
                Prepare(new CameraScene(eye, -NVector3.Normalize(eye), NVector3.UnitY, 48, 120, spectator.View.Entities));
                preview = null;
            }
            else preview = simulation.Observe(view);
            offscreen = false;
            screenshotPending = options.ExitAfterFrames is { } max && displayedFrames + 1 >= max && options.Screenshot is not null;
            RawTick(TimeSpan.Zero, 1, 0, true);
            displayedFrames++;
            if (options.ExitAfterFrames is { } limit && displayedFrames >= limit) Exit();
        }
        else if (options.ExitAfterFrames is { } limit)
        {
            var state = simulation.Snapshot();
            if (state.Frame >= limit || state.View.Finished) Exit();
        }
        // No wall-clock engine update here: idle/rendering must never advance Bepu or animations.
        // Wake immediately for RPC work; Sleep(1) can otherwise cost an entire Windows timer quantum per request.
        dispatcher.WaitForWork(options.Headless && options.Manual ? 50 : 8);
    }

    private void AdvancePhysics(TimeSpan delta) => RawTick(delta, 1, 0, false);

    public RgbImage Render(CameraScene scene)
    {
        if (Environment.CurrentManagedThreadId != ownerThread)
            throw new InvalidOperationException("Stride rendering must run on the engine thread.");
        var start = Stopwatch.GetTimestamp();
        Prepare(scene);
        offscreen = true;
        rendered = null;
        RawTick(TimeSpan.Zero, 1, 0, true);
        images++;
        imageMilliseconds += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return rendered ?? throw new InvalidOperationException("GPU image rendering did not complete.");
    }

    private void Prepare(CameraScene scene)
    {
        activeScene = scene;
        var present = scene.Entities.Select(e => e.Id).ToHashSet();
        foreach (var entry in models)
            entry.Value.Entity.Get<ModelComponent>().Enabled = present.Contains(entry.Key);
        foreach (var item in scene.Entities)
        {
            if (!models.TryGetValue(item.Id, out var entry))
            {
                var model = CreateModel(item);
                var entity = new Entity($"visual-{item.Id}") { new ModelComponent(model) };
                entity.Scene = SceneSystem.SceneInstance.RootScene;
                entry = (item, entity);
                models.Add(item.Id, entry);
            }
            else if (entry.Definition.Size != item.Size || entry.Definition.Color != item.Color || entry.Definition.Shape != item.Shape)
            {
                entry.Entity.Get<ModelComponent>().Model = CreateModel(item);
                models[item.Id] = (item, entry.Entity);
            }
            entry.Entity.Get<ModelComponent>().Enabled = true;
            entry.Entity.Transform.Position = BepuCharacterWorld.ToStride(item.Position);
            // Box geometry remains axis aligned; capsules are rotationally symmetric.
        }
        camera.ViewMatrix = Matrix.LookAtRH(BepuCharacterWorld.ToStride(scene.Eye),
            BepuCharacterWorld.ToStride(scene.Eye + scene.Forward), BepuCharacterWorld.ToStride(scene.Up));
        camera.VerticalFieldOfView = scene.VerticalFovDegrees;
        camera.FarClipPlane = scene.FarPlane;
    }

    private Model CreateModel(SceneEntity item)
    {
        PrimitiveProceduralModelBase primitive = item.Shape switch
        {
            PrimitiveShape.Sphere => new SphereProceduralModel { Radius = item.Size.X / 2 },
            PrimitiveShape.Capsule => new CapsuleProceduralModel { Radius = item.Size.X / 2, Length = item.Size.Y - item.Size.X },
            _ => new CubeProceduralModel { Size = BepuCharacterWorld.ToStride(item.Size) }
        };
        var model = primitive.Generate(Services);
        var color = new Color(item.Color.R, item.Color.G, item.Color.B, item.Color.A);
        model.Materials.Add(Material.New(GraphicsDevice, new MaterialDescriptor
        {
            Attributes =
            {
                Diffuse = new MaterialDiffuseMapFeature(new ComputeColor(color)),
                DiffuseModel = new MaterialDiffuseLambertModelFeature()
            }
        }));
        foreach (var mesh in model.Meshes)
        {
            foreach (var vertex in mesh.Draw.VertexBuffers) resources.Add(vertex.Buffer);
            if (mesh.Draw.IndexBuffer is { } index) resources.Add(index.Buffer);
        }
        return model;
    }

    protected override void Draw(GameTime time)
    {
        var command = GraphicsContext.CommandList;
        if (offscreen)
        {
            command.SetRenderTargetAndViewport(imageDepth, imageTarget);
            if (activeScene.Blind) command.Clear(imageTarget, Color4.Black);
            else
            {
                camera.AspectRatio = ImageWidth / (float)ImageHeight;
                SceneSystem.Draw(time);
            }
            // Reuse staging storage. Synchronous readback is the frame barrier for this prototype.
            imageTarget.GetData(command, readback, rgba);
            var rgb = new byte[ImageWidth * ImageHeight * 3];
            for (var pixel = 0; pixel < ImageWidth * ImageHeight; pixel++)
            {
                rgb[pixel * 3] = rgba[pixel * 4];
                rgb[pixel * 3 + 1] = rgba[pixel * 4 + 1];
                rgb[pixel * 3 + 2] = rgba[pixel * 4 + 2];
            }
            rendered = new RgbImage(ImageWidth, ImageHeight, rgb);
            return;
        }

        var backBuffer = GraphicsDevice.Presenter.BackBuffer;
        command.SetRenderTargetAndViewport(GraphicsDevice.Presenter.DepthStencilBuffer, backBuffer);
        var width = backBuffer.Width;
        var height = backBuffer.Height;
        if (preview is null)
        {
            camera.AspectRatio = width / (float)height;
            SceneSystem.Draw(time);
        }
        else
        {
            command.Clear(backBuffer, new Color4(0.025f, 0.035f, 0.055f, 1));
            if (!ReferenceEquals(lastUploaded, preview.Image))
            {
                var rgb = preview.Image.Pixels.Span;
                for (var i = 0; i < ImageWidth * ImageHeight; i++)
                {
                    rgba[i * 4] = rgb[i * 3]; rgba[i * 4 + 1] = rgb[i * 3 + 1];
                    rgba[i * 4 + 2] = rgb[i * 3 + 2]; rgba[i * 4 + 3] = 255;
                }
                previewTexture.SetData(command, rgba);
                lastUploaded = preview.Image;
            }
            var scale = MathF.Min((width - 48f) / ImageWidth, (height - 200f) / ImageHeight);
            sprites.Begin(GraphicsContext, samplerState: GraphicsDevice.SamplerStates.PointClamp,
                depthStencilState: DepthStencilStates.None);
            sprites.Draw(previewTexture, new RectangleF((width - ImageWidth * scale) / 2, 100,
                ImageWidth * scale, ImageHeight * scale), Color4.White);
            sprites.End();
        }
        DrawHud(height);
        // HUD is rendered only into the presentation target, never into observation textures.
        DebugTextSystem.Draw(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1d / 60)));
        if (screenshotPending)
        {
            var path = Path.GetFullPath(options.Screenshot!);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var file = File.Create(path);
            backBuffer.Save(command, file, ImageFileType.Png);
            screenshotPending = false;
        }
    }

    private void DrawHud(int height)
    {
        var frame = preview?.Frame ?? spectator!.Frame;
        var episode = preview?.Episode ?? spectator!.Episode;
        var title = preview is null ? "SPECTATOR" : $"AGENT {view} / EXACT RGB24 FIRST-PERSON OBSERVATION";
        DebugTextSystem.Print("ARENA  /  STRIDE 4.4 + BEPU", new Int2(24, 20), Color.White);
        DebugTextSystem.Print(title, new Int2(24, 44), Color.Aquamarine);
        DebugTextSystem.Print($"FRAME {frame:00000}   EPISODE {episode}   {(options.Manual ? "MANUAL" : paused ? "PAUSED" : "LIVE")}", new Int2(24, 68));
        if (preview is null)
            DebugTextSystem.Print($"{spectator!.View.Phase}  {spectator.View.RemainingTicks / (float)spectator.TickRate:0.0}s  {spectator.View.Winner}", new Int2(24, 100));
        DebugTextSystem.Print($"TAB: view   {(options.Manual ? "N: step" : "SPACE: pause")}   R: reset   ESC: exit   RMB / wheel: spectator camera", new Int2(24, height - 54), Color.White);
        DebugTextSystem.Print($"gRPC {options.Endpoint}  |  320x180 RGB24  |  {world.SubstepsPerDecision} physics steps / decision", new Int2(24, height - 30));
    }

    private void HandleInput()
    {
        if (Input.IsKeyPressed(Keys.Escape)) Exit();
        if (Input.IsKeyPressed(Keys.Tab)) view = (view + 1) % (simulation.Agents.Count + 1);
        if (Input.IsKeyPressed(Keys.Space)) paused = !paused;
        if (Input.IsKeyPressed(Keys.R)) simulation.Reset(simulation.Snapshot().Seed);
        if (options.Manual && Input.IsKeyPressed(Keys.N)) simulation.Tick();
        if (view == 0 && Input.IsMouseButtonDown(MouseButton.Right))
        {
            yaw -= Input.MouseDelta.X * 6;
            elevation = Math.Clamp(elevation + Input.MouseDelta.Y * 4, 0.28f, 1.4f);
        }
        if (view == 0) distance = Math.Clamp(distance - Input.MouseWheelDelta * 1.6f, 14, 52);
    }

    private T Own<T>(T value) where T : IDisposable { resources.Add(value); return value; }

    // Offscreen observation passes never present an untouched/undefined window back buffer.
    protected override void EndDraw(bool present) => base.EndDraw(present && !offscreen);

    protected override void Destroy()
    {
        world?.Dispose();
        foreach (var resource in resources.AsEnumerable().Reverse()) resource.Dispose();
        resources.Clear();
        base.Destroy();
    }
}
