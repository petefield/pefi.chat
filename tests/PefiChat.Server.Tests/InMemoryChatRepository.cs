using PefiChat.Chat;

internal sealed class InMemoryChatRepository : IChatRepository
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, ChatUser> users = [];
    private readonly Dictionary<string, List<ChatMessage>> messages = [];

    public Task SaveUserAsync(ChatUser user)
    {
        lock (gate) users.TryAdd(user.Id, user);
        return Task.CompletedTask;
    }

    public Task<ChatUser?> GetUserAsync(string id)
    {
        lock (gate) return Task.FromResult(users.GetValueOrDefault(id));
    }

    public Task<ProfileUpdateResult> UpdateProfileAsync(string id, string username, string? picture)
    {
        lock (gate)
        {
            if (!users.TryGetValue(id, out var old))
                return Task.FromResult(new ProfileUpdateResult(false, "Your profile could not be found."));
            if (users.Values.Any(u => u.Id != id && string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult(new ProfileUpdateResult(false, "That username is already in use."));
            users[id] = old with { Name = username, Username = username, Picture = picture ?? old.Picture };
            return Task.FromResult(new ProfileUpdateResult(true, null, old));
        }
    }

    public Task<IReadOnlyList<ChatUser>> GetUsersAsync()
    {
        lock (gate) return Task.FromResult<IReadOnlyList<ChatUser>>(users.Values.ToArray());
    }

    public Task<bool> UserExistsAsync(string id)
    {
        lock (gate) return Task.FromResult(users.ContainsKey(id));
    }

    public Task SaveMessageAsync(string conversationId, ChatMessage message)
    {
        lock (gate)
        {
            if (!messages.TryGetValue(conversationId, out var history))
                messages[conversationId] = history = [];
            history.Add(message);
        }
        return Task.CompletedTask;
    }

    public Task<bool> SaveDirectMessageAsync(string conversationId, ChatMessage message)
    {
        lock (gate)
        {
            if (!messages.TryGetValue(conversationId, out var history))
                messages[conversationId] = history = [];
            var first = !history.Any(previous => previous.SenderId == message.SenderId);
            history.Add(message);
            return Task.FromResult(first);
        }
    }

    public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string conversationId)
    {
        lock (gate)
            return Task.FromResult<IReadOnlyList<ChatMessage>>(
                messages.TryGetValue(conversationId, out var history) ? history.ToArray() : []);
    }
}
