using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using PefiChat.Chat;
using PefiChat.Infrastructure.Chat;

internal static class CosmosChecks
{
    public static async Task RunAsync(string endpoint)
    {
        var key = Environment.GetEnvironmentVariable("COSMOS_TEST_KEY");
        var options = new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway };
        using var client = string.IsNullOrWhiteSpace(key)
            ? new CosmosClient(endpoint, new AzureCliCredential(), options)
            : new CosmosClient(endpoint, key, options);
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Cosmos:Database"] = "pefi-chat" }).Build();
        var repository = new CosmosChatRepository(client, config);
        var id = Guid.NewGuid().ToString("N");
        var secondId = Guid.NewGuid().ToString("N");
        var user = new ChatUser(id, "Persistence test");
        var message = new ChatMessage(id, user.Name, "Persistence test", DateTimeOffset.UtcNow);
        var conversation = "test-" + id;
        var raceConversation = "test-race-" + id;
        var userContainer = client.GetContainer("pefi-chat", "users");
        var messageContainer = client.GetContainer("pefi-chat", "messages");

        try
        {
            await repository.SaveUserAsync(user);
            await repository.SaveUserAsync(new ChatUser(secondId, "Second test"));
            if (!await repository.UserExistsAsync(id) || !(await repository.GetUsersAsync()).Any(u => u.Id == id))
                throw new Exception("Cosmos user round-trip failed.");
            var name = "test_" + id[..20];
            var attempts = await Task.WhenAll(repository.UpdateProfileAsync(id, name, null),
                repository.UpdateProfileAsync(secondId, name.ToUpperInvariant(), null));
            if (attempts.Count(result => result.Success) != 1)
                throw new Exception("Cosmos unique username reservation failed.");
            var winner = attempts[0].Success ? id : secondId;
            await repository.SaveUserAsync(new ChatUser(winner, "Overwriting name"));
            if ((await repository.GetUserAsync(winner))?.Username is null)
                throw new Exception("Cosmos reconnect overwrote profile.");
            var renamed = await repository.UpdateProfileAsync(winner, "renamed_" + id[..20], null);
            if (!renamed.Success || !(await repository.UpdateProfileAsync(winner == id ? secondId : id, name, null)).Success)
                throw new Exception("Cosmos username reservation release failed.");
            if ((await repository.GetUsersAsync()).Any(u => u.Id.StartsWith("username:", StringComparison.Ordinal)))
                throw new Exception("Cosmos directory contains username reservations.");
            await repository.SaveMessageAsync(conversation, message);
            var reloaded = new CosmosChatRepository(client, config);
            var actual = (await reloaded.GetMessagesAsync(conversation)).Single();
            if (actual.Text != message.Text || actual.SenderId != id)
                throw new Exception("Cosmos message round-trip failed.");
            if (await repository.SaveDirectMessageAsync(conversation, message))
                throw new Exception("Cosmos existing message history was treated as first contact.");
            var other = message with { SenderId = secondId };
            if (!await repository.SaveDirectMessageAsync(conversation, other) ||
                await repository.SaveDirectMessageAsync(conversation, other))
                throw new Exception("Cosmos directional first-contact detection failed.");
            var results = await Task.WhenAll(Enumerable.Range(0, 10)
                .Select(_ => repository.SaveDirectMessageAsync(raceConversation, message)));
            if (results.Count(first => first) != 1 || (await repository.GetMessagesAsync(raceConversation)).Count != 10)
                throw new Exception("Cosmos concurrent first-contact detection failed.");
            Console.WriteLine("PASS: Cosmos user and message round-trip survives repository recreation");
        }
        finally
        {
            using var iterator = messageContainer.GetItemQueryIterator<dynamic>(
                new QueryDefinition("SELECT c.id FROM c"),
                requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(conversation) });
            while (iterator.HasMoreResults)
            {
                foreach (var item in await iterator.ReadNextAsync())
                    await messageContainer.DeleteItemAsync<dynamic>((string)item.id, new PartitionKey(conversation));
            }
            using var raceIterator = messageContainer.GetItemQueryIterator<dynamic>(
                new QueryDefinition("SELECT c.id FROM c"),
                requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(raceConversation) });
            while (raceIterator.HasMoreResults)
                foreach (var item in await raceIterator.ReadNextAsync())
                    await messageContainer.DeleteItemAsync<dynamic>((string)item.id, new PartitionKey(raceConversation));
            if (await repository.UserExistsAsync(id))
                await userContainer.DeleteItemAsync<dynamic>(id, new PartitionKey("users"));
            if (await repository.UserExistsAsync(secondId))
                await userContainer.DeleteItemAsync<dynamic>(secondId, new PartitionKey("users"));
            using var reservations = userContainer.GetItemQueryIterator<dynamic>(
                new QueryDefinition("SELECT c.id FROM c WHERE c.userId = @first OR c.userId = @second")
                    .WithParameter("@first", id).WithParameter("@second", secondId),
                requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey("users") });
            while (reservations.HasMoreResults)
                foreach (var item in await reservations.ReadNextAsync())
                    await userContainer.DeleteItemAsync<dynamic>((string)item.id, new PartitionKey("users"));
        }
    }
}
