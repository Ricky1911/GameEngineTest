using System.Numerics;

namespace Arena.Engine;

/// <summary>Small geometry helpers; a full physics engine can be substituted later.</summary>
public static class Geometry
{
    public static bool OverlapsXZ(Vector3 position, Vector3 size, SceneEntity box) =>
        MathF.Abs(position.X - box.Position.X) < (size.X + box.Size.X) / 2 - 0.0001f &&
        MathF.Abs(position.Z - box.Position.Z) < (size.Z + box.Size.Z) / 2 - 0.0001f;

    public static bool SegmentIntersectsBox(Vector3 start, Vector3 end, SceneEntity box)
    {
        var min = box.Position - box.Size / 2;
        var max = box.Position + box.Size / 2;
        var direction = end - start;
        var near = 0f;
        var far = 1f;
        return Slab(start.X, direction.X, min.X, max.X, ref near, ref far)
            && Slab(start.Y, direction.Y, min.Y, max.Y, ref near, ref far)
            && Slab(start.Z, direction.Z, min.Z, max.Z, ref near, ref far);
    }

    private static bool Slab(float origin, float direction, float min, float max, ref float near, ref float far)
    {
        if (MathF.Abs(direction) < 0.000001f) return origin >= min && origin <= max;
        var a = (min - origin) / direction;
        var b = (max - origin) / direction;
        near = MathF.Max(near, MathF.Min(a, b));
        far = MathF.Min(far, MathF.Max(a, b));
        return near <= far;
    }

    public static Vector3 MoveOnGround(Vector3 position, Vector3 size, Vector3 displacement,
        Vector2 arenaSize, IReadOnlyList<SceneEntity> obstacles)
    {
        // Substeps prevent tunnelling at low tick rates. Sliding is resolved X, then Z.
        var steps = Math.Max(1, (int)MathF.Ceiling(displacement.Length() / 0.15f));
        var delta = displacement / steps;
        for (var i = 0; i < steps; i++)
        {
            var candidate = position with { X = Math.Clamp(position.X + delta.X, -(arenaSize.X - size.X) / 2, (arenaSize.X - size.X) / 2) };
            if (!obstacles.Any(box => box.Solid && OverlapsXZ(candidate, size, box))) position = candidate;
            candidate = position with { Z = Math.Clamp(position.Z + delta.Z, -(arenaSize.Y - size.Z) / 2, (arenaSize.Y - size.Z) / 2) };
            if (!obstacles.Any(box => box.Solid && OverlapsXZ(candidate, size, box))) position = candidate;
        }
        return position;
    }
}
