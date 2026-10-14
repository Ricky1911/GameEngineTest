using System.Numerics;
using Arena.Engine;

namespace Arena.Rendering;

/// <summary>
/// Small CPU ray renderer for box/sphere scenes. Works without a window, GPU or native graphics calls.
/// Occlusion is resolved per pixel against the entire scene, not by filtering entity centers.
/// </summary>
public sealed class SoftwareCameraRenderer : IObservationRenderer
{
    private readonly int width;
    private readonly int height;

    public SoftwareCameraRenderer(int width = 320, int height = 180)
    {
        if (width is < 1 or > 1920 || height is < 1 or > 1080)
            throw new ArgumentOutOfRangeException(nameof(width), "Camera image must be 1..1920 by 1..1080.");
        this.width = width;
        this.height = height;
    }

    public RgbImage Render(CameraScene scene)
    {
        var pixels = new byte[width * height * 3];
        if (scene.Blind) return new RgbImage(width, height, pixels);
        var forward = Vector3.Normalize(scene.Forward);
        var right = Vector3.Normalize(Vector3.Cross(forward, scene.Up));
        var up = Vector3.Cross(right, forward);
        var halfHeight = MathF.Tan(scene.VerticalFovDegrees * MathF.PI / 360);
        var halfWidth = halfHeight * width / height;
        var objects = scene.Entities.Select(e => new RenderObject(e)).ToArray();

        for (var y = 0; y < height; y++)
        {
            var row = forward + up * ((1 - 2 * (y + 0.5f) / height) * halfHeight);
            for (var x = 0; x < width; x++)
            {
                var direction = Vector3.Normalize(row + right * ((2 * (x + 0.5f) / width - 1) * halfWidth));
                var inverse = new Vector3(Inverse(direction.X), Inverse(direction.Y), Inverse(direction.Z));
                var nearest = scene.FarPlane;
                RenderObject? hit = null;
                var normal = Vector3.Zero;
                foreach (var obj in objects)
                {
                    if (Intersect(scene.Eye, direction, inverse, obj, out var distance, out var surfaceNormal)
                        && distance >= 0.001f && distance < nearest)
                    {
                        nearest = distance;
                        hit = obj;
                        normal = surfaceNormal;
                    }
                }
                var color = new Rgba(18, 28, 46);
                if (hit is { } surface)
                {
                    var shade = 0.70f + 0.18f * MathF.Max(0, normal.Y) + 0.12f * MathF.Abs(normal.X);
                    color = surface.Entity.Color;
                    // World-space floor grid is purely visual; no coordinates or object IDs are encoded.
                    if (surface.Entity.Kind == "floor")
                    {
                        var point = scene.Eye + direction * nearest;
                        var edgeX = MathF.Abs(point.X - MathF.Round(point.X));
                        var edgeZ = MathF.Abs(point.Z - MathF.Round(point.Z));
                        if (MathF.Min(edgeX, edgeZ) < 0.025f) color = new Rgba(46, 63, 78);
                    }
                    color = new Rgba((byte)(color.R * shade), (byte)(color.G * shade), (byte)(color.B * shade));
                }
                var offset = (y * width + x) * 3;
                pixels[offset] = color.R;
                pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.B;
            }
        }
        return new RgbImage(width, height, pixels);
    }

    private static float Inverse(float value) => MathF.Abs(value) < 0.0000001f ? float.PositiveInfinity : 1 / value;

    private static bool Intersect(Vector3 origin, Vector3 direction, Vector3 inverse, RenderObject obj,
        out float distance, out Vector3 normal)
    {
        if (obj.Entity.Shape == PrimitiveShape.Sphere)
        {
            var relative = origin - obj.Entity.Position;
            var b = Vector3.Dot(relative, direction);
            var c = relative.LengthSquared() - obj.Radius * obj.Radius;
            var discriminant = b * b - c;
            distance = 0;
            normal = Vector3.Zero;
            if (discriminant < 0) return false;
            var root = MathF.Sqrt(discriminant);
            distance = -b - root;
            if (distance < 0) distance = -b + root;
            if (distance < 0) return false;
            normal = Vector3.Normalize(origin + direction * distance - obj.Entity.Position);
            return true;
        }

        var near = float.NegativeInfinity;
        var far = float.PositiveInfinity;
        normal = Vector3.Zero;
        var farNormal = Vector3.Zero;
        var intersects = Clip(origin.X, inverse.X, obj.Min.X, obj.Max.X, Vector3.UnitX, ref near, ref far, ref normal, ref farNormal)
            && Clip(origin.Y, inverse.Y, obj.Min.Y, obj.Max.Y, Vector3.UnitY, ref near, ref far, ref normal, ref farNormal)
            && Clip(origin.Z, inverse.Z, obj.Min.Z, obj.Max.Z, Vector3.UnitZ, ref near, ref far, ref normal, ref farNormal);
        distance = near;
        if (near < 0) { distance = far; normal = farNormal; }
        return intersects && far >= 0;
    }

    private static bool Clip(float origin, float inverse, float min, float max, Vector3 axis,
        ref float near, ref float far, ref Vector3 normal, ref Vector3 farNormal)
    {
        if (float.IsInfinity(inverse)) return origin >= min && origin <= max;
        var a = (min - origin) * inverse;
        var b = (max - origin) * inverse;
        var enterNormal = inverse >= 0 ? -axis : axis;
        if (a > b) (a, b) = (b, a);
        if (a > near) { near = a; normal = enterNormal; }
        if (b < far) { far = b; farNormal = -enterNormal; }
        return near <= far;
    }

    private readonly struct RenderObject(SceneEntity entity)
    {
        public SceneEntity Entity { get; } = entity;
        public Vector3 Min { get; } = entity.Position - entity.Size / 2;
        public Vector3 Max { get; } = entity.Position + entity.Size / 2;
        public float Radius { get; } = entity.Size.X / 2;
    }
}
