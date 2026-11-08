namespace PefiChat.Chat;

public sealed class ChatStore(IChatRepository repository, ILogger<ChatStore> logger)
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, (ChatUser User, Func<Task> Notify)> connections = [];

    public async Task ConnectAsync(string connectionId, ChatUser user, Func<Task> notify)
    {
        await repository.SaveUserAsync(user);
        lock (gate)
        {
            connections[connectionId] = (user, notify);
        }
        await NotifyAsync();
    }

    public async Task DisconnectAsync(string connectionId)
    {
        bool removed;
        lock (gate)
        {
            removed = connections.Remove(connectionId);
        }
        if (removed)
        {
            await NotifyAsync();
        }
    }

    public async Task<IReadOnlyList<ChatContact>> UsersAsync(string excludingUserId)
    {
        var users = await repository.GetUsersAsync();
        HashSet<string> online;
        lock (gate)
        {
            online = connections.Values.Select(connection => connection.User.Id).ToHashSet();
        }
        return users.Where(user => user.Id != excludingUserId)
            .OrderBy(user => user.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(user => user.Id, StringComparer.Ordinal)
            .Select(user => new ChatContact(user.Id, user.Name, online.Contains(user.Id), user.Picture))
            .ToArray();
    }

    public async Task<IReadOnlyList<ChatMessage>> MessagesAsync(string userId, string? otherUserId)
    {
        var messages = await repository.GetMessagesAsync(ConversationKey(userId, otherUserId));
        var users = (await repository.GetUsersAsync()).ToDictionary(user => user.Id);
        return messages.Select(message => users.TryGetValue(message.SenderId, out var user)
            ? message with { SenderName = user.Name, SenderPicture = user.Picture } : message).ToArray();
    }

    public Task<ChatUser?> GetUserAsync(string id) => repository.GetUserAsync(id);

    public Task<ProfileUpdateResult> UpdateProfileAsync(string id, string username, string? picture) =>
        repository.UpdateProfileAsync(id, username, picture);

    public Task ProfileChangedAsync() => NotifyAsync();

    public async Task<string?> SendAsync(ChatUser sender, string? recipientId, string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > ChatLimits.MaximumMessageLength)
        {
            return $"Messages must contain between 1 and {ChatLimits.MaximumMessageLength} characters.";
        }

        lock (gate)
        {
            if (!connections.Values.Any(connection => connection.User.Id == sender.Id))
            {
                return "You are no longer connected. Please refresh the page.";
            }
        }

        if (recipientId is not null && (recipientId == sender.Id || !await repository.UserExistsAsync(recipientId)))
            return "That user could not be found.";

        await repository.SaveMessageAsync(ConversationKey(sender.Id, recipientId),
            new ChatMessage(sender.Id, sender.Name, text, DateTimeOffset.UtcNow));
        await NotifyAsync();
        return null;
    }

    private async Task NotifyAsync()
    {
        Func<Task>[] callbacks;
        lock (gate)
        {
            callbacks = connections.Values.Select(connection => connection.Notify).ToArray();
        }
        await Task.WhenAll(callbacks.Select(NotifyOneAsync));
    }

    private async Task NotifyOneAsync(Func<Task> callback)
    {
        try
        {
            await callback();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not notify a chat connection.");
        }
    }

    private static string ConversationKey(string userId, string? otherUserId)
    {
        if (otherUserId is null)
        {
            return "shared";
        }
        return string.CompareOrdinal(userId, otherUserId) < 0
            ? $"{userId}:{otherUserId}"
            : $"{otherUserId}:{userId}";
    }
}
