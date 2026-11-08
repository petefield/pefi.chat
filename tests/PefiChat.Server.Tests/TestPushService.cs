using PefiChat.Push;

internal sealed class TestPushService : IPushService
{
    public string? PublicKey => "test-public-key";
    public List<(string UserId, BrowserSubscription Subscription)> Subscriptions { get; } = [];
    public List<(string UserId, PushPayload Payload)> Sent { get; } = [];
    public Task SubscribeAsync(string userId, BrowserSubscription subscription, CancellationToken cancellationToken)
    {
        lock (Subscriptions) Subscriptions.Add((userId, subscription));
        return Task.CompletedTask;
    }
    public Task SendAsync(string userId, PushPayload payload, CancellationToken cancellationToken)
    {
        lock (Sent) Sent.Add((userId, payload));
        return Task.CompletedTask;
    }
}
