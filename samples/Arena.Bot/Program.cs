using Arena.Protocol;
using Grpc.Core;
using Grpc.Net.Client;

namespace Arena.Bot;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--help"))
        {
            Console.WriteLine("Arena.Bot [--url http://127.0.0.1:50051] [--token TOKEN] [--role seeker|hider] [--match --hider-token TOKEN --referee-token TOKEN] [--seed 42] [--delay-ms 0] [--max-frames 100000]");
            return 0;
        }
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        try
        {
            var options = BotOptions.Parse(args);
            using var channel = GrpcChannel.ForAddress(options.Url);
            var client = new AgentGateway.AgentGatewayClient(channel);
            return options.Match
                ? await RunMatch(client, new RefereeGateway.RefereeGatewayClient(channel), options, stop.Token)
                : await RunAgent(client, options, stop.Token);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return 0; }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.Cancelled && stop.IsCancellationRequested) { return 0; }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Bot failed: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> RunAgent(AgentGateway.AgentGatewayClient client, BotOptions options, CancellationToken stop)
    {
        var agent = new BaselineAgent(options.Role == "seeker");
        (long Episode, long Frame) lastAction = (-1, -1);
        Console.WriteLine("Vision agent connected. Input: RGB24 image only, plus episode/frame lifecycle metadata.");
        while (!stop.IsCancellationRequested)
        {
            var observation = await client.ObserveAsync(new ObserveRequest { Token = options.Token }, deadline: Deadline(), cancellationToken: stop);
            if (observation.Finished) { Print(observation); return 0; }
            if (observation.Frame >= options.MaxFrames) return 2;
            if (lastAction != (observation.Episode, observation.Frame))
            {
                try
                {
                    await client.SubmitActionAsync(agent.Decide(observation, options.Token), deadline: Deadline(), cancellationToken: stop);
                    lastAction = (observation.Episode, observation.Frame);
                    if (observation.Frame % 20 == 0) Print(observation);
                }
                // Realtime mode may advance while a request is in transit; re-observe next time.
                catch (RpcException exception) when (exception.StatusCode is StatusCode.FailedPrecondition or StatusCode.AlreadyExists) { }
            }
            await Task.Delay(Math.Max(5, options.DelayMs), stop);
        }
        return 0;
    }

    private static async Task<int> RunMatch(AgentGateway.AgentGatewayClient client, RefereeGateway.RefereeGatewayClient referee,
        BotOptions options, CancellationToken stop)
    {
        var tokens = new[] { options.Token, options.HiderToken };
        var agents = new[] { new BaselineAgent(seeker: true), new BaselineAgent(seeker: false) };
        // The runner owns the referee token; each Decide call receives only its agent's observation.
        var state = await referee.ResetAsync(new ResetRequest { Token = options.RefereeToken, Seed = options.Seed }, deadline: Deadline(), cancellationToken: stop);
        Console.WriteLine($"Image-only match started: episode {state.Episode}, seed {options.Seed}, {state.TickRate} Hz.");
        while (!state.Finished && state.Frame < options.MaxFrames)
        {
            var observations = await Task.WhenAll(tokens.Select(token => client.ObserveAsync(
                new ObserveRequest { Token = token }, deadline: Deadline(), cancellationToken: stop).ResponseAsync));
            if (observations.Any(o => o.Frame != state.Frame || o.Episode != state.Episode))
                throw new InvalidOperationException("Match runner requires a server with --manual and exclusive referee control.");
            await Task.WhenAll(observations.Select((o, index) => client.SubmitActionAsync(
                agents[index].Decide(o, tokens[index]), deadline: Deadline(), cancellationToken: stop).ResponseAsync));
            state = await referee.StepAsync(new StepRequest
            {
                Token = options.RefereeToken, Episode = state.Episode, ExpectedFrame = state.Frame, Count = 1
            }, deadline: Deadline(), cancellationToken: stop);
            if (state.Frame % state.TickRate == 0 || state.Finished) Print(state);
            if (options.DelayMs > 0) await Task.Delay(options.DelayMs, stop);
        }
        if (!state.Finished) Console.WriteLine("Frame limit reached before the round finished.");
        return state.Finished ? 0 : 2;
    }

    private static DateTime Deadline() => DateTime.UtcNow.AddSeconds(5);
    private static void Print(ImageObservation s) => Console.WriteLine(
        $"frame={s.Frame,5} image={s.Width}x{s.Height} RGB24 bytes={s.Rgb24.Length}{(s.Finished ? " episode_finished" : "")}");
    private static void Print(Snapshot s) => Console.WriteLine(
        $"[referee] frame={s.Frame,5} phase={s.Phase,-8} remaining={s.RemainingTicks / (float)s.TickRate,5:0.0}s{(s.Finished ? $" winner={s.Winner}" : "")}");
}
