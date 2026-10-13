using System.Security.Cryptography;
using Grpc.Core;

namespace Arena.Demo;

public sealed class AccessTokens
{
    public string Seeker { get; } = Read("ARENA_SEEKER_TOKEN");
    public string Hider { get; } = Read("ARENA_HIDER_TOKEN");
    public string Referee { get; } = Read("ARENA_REFEREE_TOKEN");

    public AccessTokens()
    {
        if (new[] { Seeker, Hider, Referee }.Distinct(StringComparer.Ordinal).Count() != 3)
            throw new ArgumentException("Agent and referee tokens must be distinct.");
    }

    public int AgentId(string token) => token == Seeker ? 1 : token == Hider ? 2
        : throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid agent token."));

    public void RequireReferee(string token)
    {
        if (token != Referee) throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid referee token."));
    }

    private static string Read(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } token
        ? token : Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
}
