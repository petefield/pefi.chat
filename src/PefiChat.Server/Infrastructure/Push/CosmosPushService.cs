using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using PefiChat.Push;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;

namespace PefiChat.Infrastructure.Push;

public sealed class CosmosPushService(CosmosClient client, IConfiguration configuration, ILogger<CosmosPushService> logger) : IPushService
{
    private readonly Container users = client.GetContainer(configuration["Cosmos:Database"] ?? "pefi-chat", "users");
    public string? PublicKey =>
        string.IsNullOrWhiteSpace(configuration["Push:PrivateKey"]) || string.IsNullOrWhiteSpace(configuration["Push:Subject"])
            ? null : configuration["Push:PublicKey"];

    public async Task SubscribeAsync(string userId, BrowserSubscription subscription, CancellationToken cancellationToken)
    {
        var id = "push:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subscription.Endpoint)));
        await users.UpsertItemAsync(new SubscriptionDocument(id, "users", userId, subscription.Endpoint,
            subscription.P256dh, subscription.Auth), new PartitionKey("users"), cancellationToken: cancellationToken);
    }

    public async Task SendAsync(string userId, PushPayload payload, CancellationToken cancellationToken)
    {
        var publicKey = PublicKey;
        var privateKey = configuration["Push:PrivateKey"];
        var subject = configuration["Push:Subject"];
        if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey) || string.IsNullOrWhiteSpace(subject))
            throw new InvalidOperationException("Push:PublicKey, Push:PrivateKey and Push:Subject must be configured.");

        using var iterator = users.GetItemQueryIterator<SubscriptionDocument>(
            new QueryDefinition("SELECT * FROM c WHERE STARTSWITH(c.id, 'push:') AND c.userId = @userId")
                .WithParameter("@userId", userId),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey("users") });
        using var vapid = new VapidAuthentication(publicKey, privateKey) { Subject = subject };
        var body = System.Text.Json.JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        var webPush = new PushServiceClient(http);
        var failures = new List<Exception>();
        while (iterator.HasMoreResults)
        {
            foreach (var subscription in await iterator.ReadNextAsync(cancellationToken))
            {
                try
                {
                    var target = new PushSubscription { Endpoint = subscription.Endpoint };
                    target.SetKey(PushEncryptionKeyName.P256DH, subscription.P256dh);
                    target.SetKey(PushEncryptionKeyName.Auth, subscription.Auth);
                    await webPush.RequestPushMessageDeliveryAsync(target, new PushMessage(body), vapid, cancellationToken);
                }
                catch (PushServiceClientException exception) when (exception.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
                {
                    try
                    {
                        await users.DeleteItemAsync<SubscriptionDocument>(subscription.Id, new PartitionKey("users"),
                            new ItemRequestOptions { IfMatchEtag = subscription.ETag }, cancellationToken);
                    }
                    catch (CosmosException deleteException) when (deleteException.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.PreconditionFailed)
                    {
                        logger.LogInformation("Expired push subscription was already changed or removed.");
                    }
                }
                catch (PushServiceClientException exception)
                {
                    logger.LogWarning(exception, "Push delivery failed for a subscription.");
                    failures.Add(exception);
                }
                catch (Exception exception) when (exception is HttpRequestException or IOException or CryptographicException or ArgumentException)
                {
                    logger.LogWarning(exception, "Push delivery failed for a subscription.");
                    failures.Add(exception);
                }
                catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(exception, "Push delivery timed out for a subscription.");
                    failures.Add(exception);
                }
            }
        }
        if (failures.Count > 0) throw new AggregateException("One or more push deliveries failed.", failures);
    }

    private sealed record SubscriptionDocument(
        [property: JsonProperty("id")] string Id,
        [property: JsonProperty("partitionKey")] string PartitionKey,
        [property: JsonProperty("userId")] string UserId,
        [property: JsonProperty("endpoint")] string Endpoint,
        [property: JsonProperty("p256dh")] string P256dh,
        [property: JsonProperty("auth")] string Auth,
        [property: JsonProperty("_etag")] string? ETag = null);
}
