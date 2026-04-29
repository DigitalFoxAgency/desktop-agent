namespace AgentPlatform.Application.Bridge;

public interface IBridgeChannelFactory
{
    Task<IBridgeChannel> WaitForConnectionAsync(Guid phaseRunId, TimeSpan timeout, CancellationToken cancellationToken);

    string IssueConnectToken(Guid phaseRunId, Guid tenantId);
}
