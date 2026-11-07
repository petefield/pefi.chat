using Microsoft.Extensions.Logging.Abstractions;
using PefiChat.Chat;

var store = new ChatStore(NullLogger<ChatStore>.Instance);
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
Check(store.OnlineUsers(bob.Id).Count == 1, "multiple tabs count as one online user");
Check(notifications > 0, "presence changes notify connected clients");

Check(await store.SendAsync(alice, null, " hello everyone ") is null, "shared message is accepted");
Check(store.Messages(bob.Id, null).Single().Text == "hello everyone", "shared messages are visible and trimmed");
Check(await store.SendAsync(alice, bob.Id, "private") is null, "direct message to online user is accepted");
Check(store.Messages(bob.Id, alice.Id).Single().Text == "private", "direct message is visible to recipient");
Check(store.Messages(charlie.Id, alice.Id).Count == 0, "direct messages are isolated from other conversations");
Check(store.Messages(charlie.Id, null).Count == 1, "direct messages do not appear in shared room");
Check(await store.SendAsync(alice, charlie.Id, "offline") is not null, "messages to offline users are rejected");
Check(await store.SendAsync(alice, alice.Id, "self") is not null, "messages to self are rejected");
Check(await store.SendAsync(alice, null, "  ") is not null, "empty messages are rejected");
Check(await store.SendAsync(alice, null, new string('a', 2001)) is not null, "oversized messages are rejected");
Check(await store.SendAsync(alice, null, new string('a', 2000)) is null, "maximum-length messages are accepted");

for (var index = 0; index < 205; index++)
{
    await store.SendAsync(alice, bob.Id, index.ToString());
}
var history = store.Messages(alice.Id, bob.Id);
Check(history.Count == 200 && history[0].Text == "5" && history[^1].Text == "204", "history keeps the most recent 200 messages");

await store.DisconnectAsync(aliceTab);
Check(store.OnlineUsers(bob.Id).Count == 1, "closing one tab leaves the other online");
await store.DisconnectAsync(aliceSecondTab);
Check(store.OnlineUsers(bob.Id).Count == 0, "closing the last tab removes the user");
Check(await store.SendAsync(alice, null, "disconnected") is not null, "disconnected users cannot send");

Check(await store.SendAsync(bob, null, null) is not null, "null messages are rejected");

await HubChecks.RunAsync();
