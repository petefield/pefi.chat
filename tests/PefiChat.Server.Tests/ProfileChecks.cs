using Microsoft.Extensions.Logging.Abstractions;
using PefiChat.Chat;
using PefiChat.Profiles;

internal static class ProfileChecks
{
    public static async Task RunAsync()
    {
        var repository = new InMemoryChatRepository();
        await repository.SaveUserAsync(new ChatUser("alice", "Alice"));
        await repository.SaveUserAsync(new ChatUser("bob", "Bob"));
        var chat = new ChatService(repository, NullLogger<ChatService>.Instance);
        var pictures = new TestPictureStore();
        var profiles = new ProfileService(chat, pictures);
        var notifications = 0;
        await chat.ConnectAsync("alice-connection", new ChatUser("alice", "Alice"),
            () => { notifications++; return Task.CompletedTask; });
        var beforeUpdate = notifications;
        Check(await profiles.UpdateAsync("alice", "Alice_123", [1], CancellationToken.None) is null,
            "profile service saves a valid name and picture");
        Check((await repository.GetUserAsync("alice"))?.Picture == "picture-1" && notifications > beforeUpdate,
            "profile service stores picture and notifies clients");
        Check(await profiles.UpdateAsync("bob", "alice_123", [1], CancellationToken.None) is not null &&
            pictures.Deleted.SequenceEqual(["picture-2"]),
            "profile service removes an uploaded picture after username conflict");
        Check(await profiles.UpdateAsync("alice", "Alice_123", [1], CancellationToken.None) is null &&
            pictures.Deleted.SequenceEqual(["picture-2", "picture-1"]) &&
            (await repository.GetUserAsync("alice"))?.Picture == "picture-3",
            "profile service removes the previous picture after replacement");
        Check(await profiles.UpdateAsync("alice", "!", [1], CancellationToken.None) is not null && pictures.Uploads == 3,
            "invalid usernames are rejected before uploading a picture");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception($"FAILED: {description}");
        Console.WriteLine($"PASS: {description}");
    }

    private sealed class TestPictureStore : IProfilePictureStore
    {
        public int Uploads { get; private set; }
        public List<string> Deleted { get; } = [];

        public Task<(string? Name, string? Error)> UploadAsync(byte[] bytes, CancellationToken cancellationToken) =>
            Task.FromResult<(string?, string?)>(($"picture-{++Uploads}", null));

        public Task<byte[]?> ReadAsync(string name, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string name, CancellationToken cancellationToken)
        {
            Deleted.Add(name);
            return Task.CompletedTask;
        }
    }
}
