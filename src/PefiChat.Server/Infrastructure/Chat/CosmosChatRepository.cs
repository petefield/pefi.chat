using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using PefiChat.Chat;

namespace PefiChat.Infrastructure.Chat;

public sealed class CosmosChatRepository(CosmosClient client, IConfiguration configuration) : IChatRepository
{
    private readonly Container users = client.GetContainer(configuration["Cosmos:Database"] ?? "pefi-chat", "users");
    private readonly Container messages = client.GetContainer(configuration["Cosmos:Database"] ?? "pefi-chat", "messages");

    public async Task SaveUserAsync(ChatUser user)
    {
        try
        {
            await users.CreateItemAsync(new UserDocument(user.Id, "users", user.Name), new PartitionKey("users"));
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            // An existing profile must not be overwritten by the identity provider.
        }
    }

    public async Task<ChatUser?> GetUserAsync(string id)
    {
        try
        {
            return ToUser((await users.ReadItemAsync<UserDocument>(id, new PartitionKey("users"))).Resource);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<ProfileUpdateResult> UpdateProfileAsync(string id, string username, string? picture)
    {
        var partition = new PartitionKey("users");
        for (var attempt = 0; attempt < 5; attempt++)
        {
            ItemResponse<UserDocument> current;
            try { current = await users.ReadItemAsync<UserDocument>(id, partition); }
            catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                return new(false, "Your profile could not be found.");
            }
            var old = current.Resource;
            var newReservation = ReservationId(username);
            var oldReservation = old.Username is null ? null : ReservationId(old.Username);
            var updated = old with { Name = username, Username = username, Picture = picture ?? old.Picture };
            var batch = users.CreateTransactionalBatch(partition);
            if (newReservation != oldReservation)
                batch.CreateItem(new UsernameReservation(newReservation, "users", id));
            batch.ReplaceItem(id, updated, new TransactionalBatchItemRequestOptions { IfMatchEtag = current.ETag });
            if (oldReservation is not null && oldReservation != newReservation)
                batch.DeleteItem(oldReservation);
            using var response = await batch.ExecuteAsync();
            if (response.IsSuccessStatusCode)
                return new(true, null, ToUser(old));
            if (response.StatusCode == HttpStatusCode.Conflict)
                return new(false, "That username is already in use.");
            if (response.StatusCode == HttpStatusCode.PreconditionFailed)
                continue;
            throw new InvalidOperationException($"Could not update profile: Cosmos returned {response.StatusCode}.");
        }
        return new(false, "Your profile changed elsewhere. Please try again.");
    }

    private static string ReservationId(string username) =>
        "username:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(username.ToUpperInvariant())));

    private static ChatUser ToUser(UserDocument user) => new(user.Id, user.Name, user.Picture, user.Username);

    public async Task<IReadOnlyList<ChatUser>> GetUsersAsync()
    {
        var result = new List<ChatUser>();
        using var iterator = users.GetItemQueryIterator<UserDocument>(
            new QueryDefinition("SELECT * FROM c WHERE IS_DEFINED(c.name)"),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey("users") });
        while (iterator.HasMoreResults)
        {
            foreach (var user in await iterator.ReadNextAsync())
                result.Add(ToUser(user));
        }
        return result;
    }

    public async Task<bool> UserExistsAsync(string id)
    {
        try
        {
            await users.ReadItemAsync<UserDocument>(id, new PartitionKey("users"));
            return true;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task SaveMessageAsync(string conversationId, ChatMessage message) =>
        await messages.CreateItemAsync(new MessageDocument(Guid.NewGuid().ToString("N"), conversationId,
            message.SenderId, message.SenderName, message.Text, message.SentAt),
            new PartitionKey(conversationId));

    public async Task<bool> SaveDirectMessageAsync(string conversationId, ChatMessage message)
    {
        var partition = new PartitionKey(conversationId);
        var markerId = "sender:" + message.SenderId;
        try
        {
            await messages.ReadItemAsync<SenderMarker>(markerId, partition);
            await SaveMessageAsync(conversationId, message);
            return false;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // Older conversations may have messages but no marker.
        }

        using var history = messages.GetItemQueryIterator<int>(
            new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.senderId = @sender")
                .WithParameter("@sender", message.SenderId),
            requestOptions: new QueryRequestOptions { PartitionKey = partition });
        var previouslySent = false;
        while (history.HasMoreResults)
            previouslySent |= (await history.ReadNextAsync()).Any(count => count > 0);

        var document = new MessageDocument(Guid.NewGuid().ToString("N"), conversationId,
            message.SenderId, message.SenderName, message.Text, message.SentAt);
        using var response = await messages.CreateTransactionalBatch(partition)
            .CreateItem(new SenderMarker(markerId, conversationId))
            .CreateItem(document)
            .ExecuteAsync();
        if (response.IsSuccessStatusCode) return !previouslySent;
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            await SaveMessageAsync(conversationId, message);
            return false;
        }
        throw new InvalidOperationException($"Could not save direct message: Cosmos returned {response.StatusCode}.");
    }

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string conversationId)
    {
        var result = new List<ChatMessage>();
        using var iterator = messages.GetItemQueryIterator<MessageDocument>(
            new QueryDefinition("SELECT * FROM c WHERE IS_DEFINED(c.sentAt) ORDER BY c.sentAt ASC"),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(conversationId) });
        while (iterator.HasMoreResults)
        {
            foreach (var message in await iterator.ReadNextAsync())
                result.Add(new ChatMessage(message.SenderId, message.SenderName, message.Text, message.SentAt));
        }
        return result;
    }

    private sealed record UserDocument(
        [property: JsonProperty("id")] string Id,
        [property: JsonProperty("partitionKey")] string PartitionKey,
        [property: JsonProperty("name")] string Name,
        [property: JsonProperty("picture")] string? Picture = null,
        [property: JsonProperty("username")] string? Username = null);

    private sealed record UsernameReservation(
        [property: JsonProperty("id")] string Id,
        [property: JsonProperty("partitionKey")] string PartitionKey,
        [property: JsonProperty("userId")] string UserId);

    private sealed record MessageDocument(
        [property: JsonProperty("id")] string Id,
        [property: JsonProperty("partitionKey")] string PartitionKey,
        [property: JsonProperty("senderId")] string SenderId,
        [property: JsonProperty("senderName")] string SenderName,
        [property: JsonProperty("text")] string Text,
        [property: JsonProperty("sentAt")] DateTimeOffset SentAt);

    private sealed record SenderMarker(
        [property: JsonProperty("id")] string Id,
        [property: JsonProperty("partitionKey")] string PartitionKey);
}
