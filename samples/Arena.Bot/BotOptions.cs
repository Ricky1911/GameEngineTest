namespace Arena.Bot;

internal sealed record BotOptions
{
    public string Url { get; init; } = "http://127.0.0.1:50051";
    public bool Match { get; init; }
    public string Token { get; init; } = Environment.GetEnvironmentVariable("ARENA_SEEKER_TOKEN") ?? "";
    public string HiderToken { get; init; } = Environment.GetEnvironmentVariable("ARENA_HIDER_TOKEN") ?? "";
    public string RefereeToken { get; init; } = Environment.GetEnvironmentVariable("ARENA_REFEREE_TOKEN") ?? "";
    public int Seed { get; init; } = 42;
    public int DelayMs { get; init; }
    public int MaxFrames { get; init; } = 100000;

    public static BotOptions Parse(string[] args)
    {
        var result = new BotOptions();
        for (var i = 0; i < args.Length; i++)
        {
            string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {args[i]}.");
            int Number() => int.TryParse(Value(), out var value) ? value : throw new ArgumentException("Expected an integer.");
            result = args[i] switch
            {
                "--match" => result with { Match = true },
                "--url" => result with { Url = Value() },
                "--token" => result with { Token = Value() },
                "--hider-token" => result with { HiderToken = Value() },
                "--referee-token" => result with { RefereeToken = Value() },
                "--seed" => result with { Seed = Number() },
                "--delay-ms" => result with { DelayMs = Number() },
                "--max-frames" => result with { MaxFrames = Number() },
                _ => throw new ArgumentException($"Unknown option: {args[i]}")
            };
        }
        if (string.IsNullOrWhiteSpace(result.Token))
            throw new ArgumentException("Provide --token or ARENA_SEEKER_TOKEN from the server console.");
        if (result.Match && (string.IsNullOrWhiteSpace(result.HiderToken) || string.IsNullOrWhiteSpace(result.RefereeToken)))
            throw new ArgumentException("Match runner requires --hider-token and --referee-token (or matching ARENA_* environment variables).");
        if (result.DelayMs is < 0 or > 60000 || result.MaxFrames < 1)
            throw new ArgumentException("Delay must be 0..60000 ms; max frames must be positive.");
        return result;
    }
}
