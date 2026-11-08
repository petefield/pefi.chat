using Microsoft.Extensions.Logging.Abstractions;
using PefiChat.Chat;

var repository = new InMemoryChatRepository();
var store = new ChatService(repository, NullLogger<ChatService>.Instance);
var alice = new ChatUser("alice", "Alice");
var bob = new ChatUser("bob", "Bob");
var charlie = new ChatUser("charlie", "Charlie");
var aliceTab = Guid.NewGuid().ToString();
var aliceSecondTab = Guid.NewGuid().ToString();
var bobTab = Guid.NewGuid().ToString();
var notifications = 0;
Task Notify() { Interlocked.Increment(ref notifications); return Task.CompletedTask; }
void Check(bool condition, string description)
{
    if (!condition) throw new Exception($"FAILED: {description}");
    Console.WriteLine($"PASS: {description}");
}

await store.ConnectAsync(aliceTab, alice, Notify);
await store.ConnectAsync(aliceSecondTab, alice, Notify);
await store.ConnectAsync(bobTab, bob, Notify);
Check((await store.GetUsersAsync(bob.Id)).Count == 1, "multiple tabs count as one user");
Check(notifications > 0, "presence changes notify connected clients");

Check(await store.SendMessageAsync(alice, null, " hello everyone ") is null, "shared message is accepted");
Check((await store.GetMessagesAsync(bob.Id, null)).Single().Text == "hello everyone", "shared messages are visible and trimmed");
Check(await store.SendMessageAsync(alice, bob.Id, "private") is null, "direct message to online user is accepted");
Check((await store.GetMessagesAsync(bob.Id, alice.Id)).Single().Text == "private", "direct message is visible to recipient");
Check((await store.GetMessagesAsync(charlie.Id, alice.Id)).Count == 0, "direct messages are isolated from other conversations");
Check((await store.GetMessagesAsync(charlie.Id, null)).Count == 1, "direct messages do not appear in shared room");
Check(await store.SendMessageAsync(alice, charlie.Id, "offline") is not null, "messages to unknown users are rejected");
Check(await store.SendMessageAsync(alice, alice.Id, "self") is not null, "messages to self are rejected");
Check(await store.SendMessageAsync(alice, null, "  ") is not null, "empty messages are rejected");
Check(await store.SendMessageAsync(alice, null, new string('a', 2001)) is not null, "oversized messages are rejected");
Check(await store.SendMessageAsync(alice, null, new string('a', 2000)) is null, "maximum-length messages are accepted");

for (var index = 0; index < 205; index++)
{
    await store.SendMessageAsync(alice, bob.Id, index.ToString());
}
var history = await store.GetMessagesAsync(alice.Id, bob.Id);
Check(history.Count == 206 && history[0].Text == "private" && history[^1].Text == "204", "history retains more than 200 messages");

await store.DisconnectAsync(aliceTab);
Check((await store.GetUsersAsync(bob.Id)).Single().IsOnline, "closing one tab leaves the other online");
await store.DisconnectAsync(aliceSecondTab);
Check(!(await store.GetUsersAsync(bob.Id)).Single().IsOnline, "closing the last tab marks the user offline");
Check(await store.SendMessageAsync(bob, alice.Id, "offline delivery") is null, "messages to known offline users are accepted");
var restarted = new ChatService(repository, NullLogger<ChatService>.Instance);
Check((await restarted.GetMessagesAsync(alice.Id, bob.Id)).Last().Text == "offline delivery", "messages survive store restart");
Check((await restarted.GetUsersAsync(bob.Id)).Single().Id == alice.Id, "users survive store restart");
Check(await store.SendMessageAsync(alice, null, "disconnected") is not null, "disconnected users cannot send");

Check(await store.SendMessageAsync(bob, null, null) is not null, "null messages are rejected");

var updated = await repository.UpdateProfileAsync(alice.Id, "Alice_123", "picture-id");
Check(updated.Success, "profile can be updated");
await store.ConnectAsync(aliceTab, new ChatUser(alice.Id, "New SSO name"), Notify);
var profile = await repository.GetUserAsync(alice.Id);
Check(profile?.Name == "Alice_123" && profile.Picture == "picture-id", "reconnecting preserves the chosen profile");
Check((await store.GetMessagesAsync(bob.Id, null)).First().SenderName == "Alice_123", "historical messages use the current username");
Check(!(await repository.UpdateProfileAsync(bob.Id, "alice_123", null)).Success, "usernames are case-insensitively unique");
var simultaneous = await Task.WhenAll(repository.UpdateProfileAsync(alice.Id, "shared_name", null),
    repository.UpdateProfileAsync(bob.Id, "SHARED_NAME", null));
Check(simultaneous.Count(result => result.Success) == 1, "concurrent username claims have one winner");

await HubChecks.RunAsync();
await EndpointChecks.RunAsync();
await ProfileChecks.RunAsync();

var cosmosEndpoint = Environment.GetEnvironmentVariable("COSMOS_TEST_ENDPOINT");
if (!string.IsNullOrWhiteSpace(cosmosEndpoint))
    await CosmosChecks.RunAsync(cosmosEndpoint);
var blobEndpoint = Environment.GetEnvironmentVariable("BLOB_TEST_ENDPOINT");
if (!string.IsNullOrWhiteSpace(blobEndpoint))
    await BlobChecks.RunAsync(blobEndpoint);
