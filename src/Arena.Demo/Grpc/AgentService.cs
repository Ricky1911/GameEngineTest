using Arena.Engine;
using Arena.Protocol;
using Grpc.Core;

namespace Arena.Demo.Grpc;

public sealed class AgentService(GameSimulation simulation, AccessTokens tokens) : AgentGateway.AgentGatewayBase
{
    public override Task<Snapshot> Observe(ObserveRequest request, ServerCallContext context) =>
        ProtocolMapping.Invoke(() => ProtocolMapping.Map(simulation.Snapshot(tokens.AgentId(request.Token))));

    public override Task<ActionReceipt> SubmitAction(ActionRequest request, ServerCallContext context) =>
        ProtocolMapping.Invoke(() =>
        {
            var agentId = tokens.AgentId(request.Token);
            var move = request.Move is null ? System.Numerics.Vector3.Zero
                : new System.Numerics.Vector3(request.Move.X, request.Move.Y, request.Move.Z);
            simulation.Submit(agentId, request.Episode, request.TargetFrame, new AgentAction(move, request.Ability));
            return new ActionReceipt { Episode = request.Episode, TargetFrame = request.TargetFrame };
        });
}
