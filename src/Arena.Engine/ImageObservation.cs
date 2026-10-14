using System.Numerics;

namespace Arena.Engine;

/// <summary>Top-to-bottom rows, left-to-right pixels, 3 bytes per pixel: R, G, B. No padding.</summary>
public sealed class RgbImage
{
    public RgbImage(int width, int height, ReadOnlySpan<byte> pixels)
    {
        if (width <= 0 || height <= 0 || pixels.Length != checked(width * height * 3))
            throw new ArgumentException("RGB24 pixels must match the positive image dimensions.");
        Width = width;
        Height = height;
        Pixels = pixels.ToArray();
    }

    public int Width { get; }
    public int Height { get; }
    public ReadOnlyMemory<byte> Pixels { get; }
}

// Episode/frame and Finished are scheduling/lifecycle metadata, not world-state observations.
// In particular this type contains no entity IDs, position, heading, map, phase, score or seed.
public sealed record ImageObservation(long Episode, long Frame, bool Finished, RgbImage Image);

// Trusted server-side input to the image renderer. Never passed to an agent or sent by AgentGateway.
public sealed record CameraScene(
    Vector3 Eye, Vector3 Forward, Vector3 Up, float VerticalFovDegrees, float FarPlane,
    IReadOnlyList<SceneEntity> Entities, bool Blind = false);

public interface IObservationRenderer
{
    RgbImage Render(CameraScene scene);
}
