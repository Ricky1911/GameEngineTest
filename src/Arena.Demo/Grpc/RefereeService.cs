using Arena.Engine;
using Arena.Protocol;
using Grpc.Core;

namespace Arena.Demo.Grpc;

public sealed class RefereeService(GameSimulation simulation, AccessTokens tokens, DemoOptions options) : RefereeGateway.RefereeGatewayBase
{
    public override Task<Snapshot> Inspect(RefereeRequest request, ServerCallContext context) =>
        ProtocolMapping.Invoke(() =>
        {
            tokens.RequireReferee(request.Token);
            return ProtocolMapping.Map(simulation.Snapshot());
        });

    public override Task<Snapshot> Step(StepRequest request, ServerCallContext context) =>
        ProtocolMapping.Invoke(() =>
        {
            tokens.RequireReferee(request.Token);
            if (!options.Manual)
                throw new RpcException(new Status(StatusCode.FailedPrecondition, "Step requires --manual mode."));
            return ProtocolMapping.Map(simulation.Advance(request.Episode, request.ExpectedFrame, request.Count));
        });

    public override Task<Snapshot> Reset(ResetRequest request, ServerCallContext context) =>
        ProtocolMapping.Invoke(() =>
        {
            tokens.RequireReferee(request.Token);
            return ProtocolMapping.Map(simulation.Reset(request.Seed));
        });
}
