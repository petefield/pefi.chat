using System.Text.Json;
using PefiChat.Chat;

var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var snapshot = new ChatSnapshot(
    new ChatUser("alice", "Alice", "alice-picture", "Alice"),
    [new ChatContact("bob", "Bob", false, "bob-picture")],
    [new ChatMessage("bob", "Bob", "Hello", DateTimeOffset.Parse("2026-01-01T12:00:00Z"), "bob-picture")]);
var json = JsonSerializer.Serialize(snapshot, options);
var restored = JsonSerializer.Deserialize<ChatSnapshot>(json, options)
    ?? throw new Exception("Snapshot deserialization returned null.");
if (restored.User != snapshot.User || restored.Users.Single() != snapshot.Users.Single() ||
    restored.Messages.Single() != snapshot.Messages.Single())
    throw new Exception("Client snapshot JSON round-trip failed.");
Console.WriteLine("PASS: client snapshot JSON round-trip preserves profile, presence and message data");

var legacy = JsonSerializer.Deserialize<ChatUser>("""{"id":"alice","name":"Alice"}""", options)
    ?? throw new Exception("Legacy user deserialization returned null.");
if (legacy.Picture is not null || legacy.Username is not null)
    throw new Exception("Legacy user optional profile fields must remain unset.");
Console.WriteLine("PASS: client accepts user data without optional profile fields");
