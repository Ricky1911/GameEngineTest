namespace Arena.Demo;

public sealed record DemoOptions
{
    public bool Headless { get; init; }
    public bool Manual { get; init; }
    public bool Bots { get; init; } = true;
    public int Port { get; init; } = 50051;
    public int TickRate { get; init; } = 20;
    public int Seed { get; init; } = 42;
    public int RoundSeconds { get; init; } = 60;
    public int? ExitAfterFrames { get; init; }
    public string? Screenshot { get; init; }

    public static DemoOptions Parse(string[] args)
    {
        var result = new DemoOptions();
        for (var i = 0; i < args.Length; i++)
        {
            string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {args[i]}.");
            int Number() => int.TryParse(Value(), out var value) ? value : throw new ArgumentException("Expected an integer.");
            result = args[i] switch
            {
                "--headless" => result with { Headless = true },
                "--manual" => result with { Manual = true },
                "--no-bots" => result with { Bots = false },
                "--port" => result with { Port = Number() },
                "--tick-rate" => result with { TickRate = Number() },
                "--seed" => result with { Seed = Number() },
                "--round-seconds" => result with { RoundSeconds = Number() },
                "--exit-after-frames" => result with { ExitAfterFrames = Number() },
                "--screenshot" => result with { Screenshot = Value() },
                _ => throw new ArgumentException($"Unknown option: {args[i]}")
            };
        }
        if (result.Port is < 1 or > 65535 || result.TickRate is < 1 or > 120 || result.RoundSeconds is < 4 or > 3600)
            throw new ArgumentException("Port must be 1..65535, tick rate 1..120, round seconds 4..3600.");
        if (result.ExitAfterFrames is < 1 || result.Headless && result.Screenshot is not null)
            throw new ArgumentException("Exit frame must be positive; screenshots require a window.");
        return result;
    }
}
