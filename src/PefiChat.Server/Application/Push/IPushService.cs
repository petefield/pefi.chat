namespace PefiChat.Push;

public sealed record BrowserSubscription(string Endpoint, string P256dh, string Auth);
public sealed record PushPayload(string Title, string Body, string Icon, string Tag, string Url);

public interface IPushService
{
    string? PublicKey { get; }
    Task SubscribeAsync(string userId, BrowserSubscription subscription, CancellationToken cancellationToken);
    Task SendAsync(string userId, PushPayload payload, CancellationToken cancellationToken);
}
