using System.Numerics;

namespace Arena.Engine;

/// <summary>Server-side character physics, independent of game rules and observation transport.</summary>
public interface ICharacterWorld
{
    void Reset(IReadOnlyList<SceneEntity> scenery, IReadOnlyList<SceneEntity> characters, int tickRate);
    void Drive(int entityId, Vector3 worldVelocity, bool jump = false);
    void Advance();
    Vector3 Position(int entityId);
    bool IsGrounded(int entityId);
    bool IsOccluded(Vector3 from, Vector3 to);
}
