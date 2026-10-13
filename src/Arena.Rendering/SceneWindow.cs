using System.Numerics;
using Arena.Engine;
using Raylib_cs;

namespace Arena.Rendering;

public sealed record WindowControls(Action TogglePause, Action Step, Action Reset, Func<bool> IsPaused, bool Manual, string Endpoint);

/// <summary>Generic snapshot renderer. No dependency on any game module or network protocol.</summary>
public static class SceneWindow
{
    private static readonly Color Background = new(12, 20, 32);
    private static readonly Color Panel = new(19, 31, 47);
    private static readonly Color Muted = new(148, 168, 190);
    private static readonly Color Accent = new(86, 226, 198);

    public static void Run(Func<int, WorldSnapshot> observe, WindowControls controls, CancellationToken cancellation,
        int? exitAfterFrames = null, string? screenshot = null)
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint);
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.InitWindow(1440, 900, "ARENA / Agent playground");
        Raylib.SetWindowMinSize(1000, 700);
        Raylib.SetTargetFPS(60);
        var yaw = 0.67f;
        var elevation = 0.82f;
        var distance = 36f;
        var observer = 0;
        var renderedFrames = 0;
        try
        {
            while (!Raylib.WindowShouldClose() && !cancellation.IsCancellationRequested)
            {
                if (Raylib.IsKeyPressed(KeyboardKey.Space)) controls.TogglePause();
                if (Raylib.IsKeyPressed(KeyboardKey.N)) controls.Step();
                if (Raylib.IsKeyPressed(KeyboardKey.R)) controls.Reset();
                if (Raylib.IsKeyPressed(KeyboardKey.Tab))
                {
                    var ids = observe(0).View.Entities.Where(e => e.AgentId != 0).Select(e => e.AgentId).Prepend(0).ToArray();
                    observer = ids[(Array.IndexOf(ids, observer) + 1) % ids.Length];
                }
                if (Raylib.IsMouseButtonDown(MouseButton.Right))
                {
                    var delta = Raylib.GetMouseDelta();
                    yaw -= delta.X * 0.006f;
                    elevation = Math.Clamp(elevation + delta.Y * 0.004f, 0.28f, 1.4f);
                }
                distance = Math.Clamp(distance - Raylib.GetMouseWheelMove() * 1.6f, 14, 52);
                var snapshot = observe(observer);
                var camera = new Camera3D(
                    new Vector3(MathF.Sin(yaw) * MathF.Cos(elevation), MathF.Sin(elevation), MathF.Cos(yaw) * MathF.Cos(elevation)) * distance,
                    new Vector3(0, 0, 0), Vector3.UnitY, 48, CameraProjection.Perspective);

                Raylib.BeginDrawing();
                Raylib.ClearBackground(Background);
                Raylib.BeginMode3D(camera);
                DrawScene(snapshot);
                Raylib.EndMode3D();
                DrawAgentMarkers(snapshot, camera);
                DrawOverlay(snapshot, controls);
                Raylib.EndDrawing();
                renderedFrames++;
                if (exitAfterFrames is { } max && renderedFrames >= max)
                {
                    if (screenshot is not null)
                    {
                        var path = Path.GetFullPath(screenshot);
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        var image = Raylib.LoadImageFromScreen();
                        try
                        {
                            if (!Raylib.ExportImage(image, path))
                                throw new IOException($"Could not save screenshot to {path}.");
                        }
                        finally { Raylib.UnloadImage(image); }
                    }
                    break;
                }
            }
        }
        finally { Raylib.CloseWindow(); }
    }

    private static void DrawScene(WorldSnapshot snapshot)
    {
        foreach (var e in snapshot.View.Entities)
        {
            var color = new Color(e.Color.R, e.Color.G, e.Color.B, e.Color.A);
            if (e.Shape == PrimitiveShape.Sphere)
                Raylib.DrawSphere(e.Position, e.Size.X / 2, color);
            else
            {
                Raylib.DrawCubeV(e.Position, e.Size, color);
                Raylib.DrawCubeWiresV(e.Position, e.Size, new Color(178, 204, 225, 100));
            }
            if (e.AgentId != 0)
            {
                Raylib.DrawSphere(e.Position + Vector3.UnitY * (e.Size.Y / 2 + 0.23f), 0.21f, color);
                Raylib.DrawCircle3D(new Vector3(e.Position.X, 0.02f, e.Position.Z), 0.7f, Vector3.UnitX, 90, color);
            }
            if (e.Kind == "floor")
            {
                for (var x = -(int)e.Size.X / 2; x <= e.Size.X / 2; x++)
                    Raylib.DrawLine3D(new(x, 0.015f, -e.Size.Z / 2), new(x, 0.015f, e.Size.Z / 2), new Color(65, 89, 112, 90));
                for (var z = -(int)e.Size.Z / 2; z <= e.Size.Z / 2; z++)
                    Raylib.DrawLine3D(new(-e.Size.X / 2, 0.015f, z), new(e.Size.X / 2, 0.015f, z), new Color(65, 89, 112, 90));
            }
        }
    }

    private static void DrawAgentMarkers(WorldSnapshot snapshot, Camera3D camera)
    {
        foreach (var actor in snapshot.View.Entities.Where(e => e.AgentId != 0))
        {
            var point = Raylib.GetWorldToScreen(actor.Position + Vector3.UnitY * (actor.Size.Y / 2 + 0.5f), camera);
            var label = $"#{actor.AgentId} {actor.Team.ToUpperInvariant()}";
            var textWidth = Raylib.MeasureText(label, 16);
            var color = new Color(actor.Color.R, actor.Color.G, actor.Color.B, actor.Color.A);
            var x = (int)point.X;
            var y = (int)point.Y;
            Raylib.DrawLine(x, y, x, y - 16, color);
            Raylib.DrawCircle(x, y, 3, color);
            Raylib.DrawRectangle(x - textWidth / 2 - 7, y - 40, textWidth + 14, 24, Panel);
            Raylib.DrawText(label, x - textWidth / 2, y - 36, 16, color);
        }
    }

    private static void DrawOverlay(WorldSnapshot s, WindowControls controls)
    {
        var width = Raylib.GetScreenWidth();
        var height = Raylib.GetScreenHeight();
        Raylib.DrawRectangle(0, 0, width, 110, Panel);
        Raylib.DrawRectangle(0, 108, width, 2, new Color(47, 73, 96));
        Raylib.DrawText("A R E N A", 30, 20, 30, Color.RayWhite);
        Raylib.DrawText("AGENT PLAYGROUND   /   3D ENGINE DEMO", 30, 65, 16, Muted);
        Raylib.DrawText(s.GameId.ToUpperInvariant(), 420, 27, 22, Accent);
        Raylib.DrawText($"{s.TickRate} Hz simulation   /   {Raylib.GetFPS()} FPS render", 420, 64, 16, Muted);
        Raylib.DrawText(controls.Manual ? "MANUAL STEP" : controls.IsPaused() ? "PAUSED" : "LIVE", width - 225, 27, 23, Accent);
        Raylib.DrawText($"EPISODE {s.Episode:00}", width - 225, 66, 16, Muted);

        Raylib.DrawRectangle(24, 133, 260, 219, Panel);
        Raylib.DrawText("MATCH STATUS", 42, 152, 16, Muted);
        Raylib.DrawText($"FRAME   {s.Frame:00000}", 42, 188, 24, Color.RayWhite);
        Raylib.DrawText($"{s.View.RemainingTicks / (float)s.TickRate:0.0}s remaining", 42, 226, 20, Accent);
        Raylib.DrawText($"Phase: {s.View.Phase}", 42, 263, 18, Muted);
        Raylib.DrawText(s.ObserverId == 0 ? "View: spectator" : $"View: agent {s.ObserverId}", 42, 301, 18, Color.RayWhite);

        var y = 385;
        foreach (var actor in s.View.Entities.Where(e => e.AgentId != 0))
        {
            Raylib.DrawRectangle(32, y, 10, 10, new Color(actor.Color.R, actor.Color.G, actor.Color.B, actor.Color.A));
            Raylib.DrawText($"#{actor.AgentId}  {actor.Team.ToUpperInvariant()}", 53, y - 4, 18, Color.RayWhite);
            y += 35;
        }
        if (s.View.Finished)
        {
            var text = $"{s.View.Winner.ToUpperInvariant()} WIN";
            Raylib.DrawRectangle(width / 2 - 260, height / 2 - 50, 520, 100, Panel);
            Raylib.DrawText(text, width / 2 - Raylib.MeasureText(text, 32) / 2, height / 2 - 26, 32, Accent);
            Raylib.DrawText("Press R for a new round", width / 2 - 130, height / 2 + 17, 18, Muted);
        }
        Raylib.DrawRectangle(0, height - 78, width, 78, Panel);
        var modeControls = controls.Manual ? "N: step" : "SPACE: pause";
        Raylib.DrawText($"RMB drag: orbit   WHEEL: zoom   TAB: view   {modeControls}   R: reset   ESC: exit", 28, height - 61, 17, Color.RayWhite);
        Raylib.DrawText($"gRPC  {controls.Endpoint}    |    {s.View.Events.LastOrDefault()?.Message ?? "Observe -> decide -> submit -> advance"}", 28, height - 29, 15, Muted);
    }
}
