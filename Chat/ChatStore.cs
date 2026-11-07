namespace PefiChat.Chat;

// All state is process-local. Access and snapshots are protected by a single lock.
public sealed class ChatStore(ILogger<ChatStore> logger)
{
    private const int HistoryLimit = 200;
    private readonly Lock gate = new();
    private readonly Dictionary<string, (ChatUser User, Func<Task> Notify)> connections = [];
    private readonly Dictionary<string, List<ChatMessage>> conversations = [];

    public async Task ConnectAsync(string connectionId, ChatUser user, Func<Task> notify)
    {
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

    public IReadOnlyList<ChatUser> OnlineUsers(string excludingUserId)
    {
        lock (gate)
        {
            return connections.Values.Select(c => c.User)
                .Where(user => user.Id != excludingUserId)
                .DistinctBy(user => user.Id)
                .OrderBy(user => user.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(user => user.Id, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public IReadOnlyList<ChatMessage> Messages(string userId, string? otherUserId)
    {
        lock (gate)
        {
            return conversations.TryGetValue(ConversationKey(userId, otherUserId), out var messages)
                ? messages.ToArray()
                : [];
        }
    }

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
            if (recipientId is not null &&
                (recipientId == sender.Id || !connections.Values.Any(connection => connection.User.Id == recipientId)))
            {
                return "That user is no longer online.";
            }

            var key = ConversationKey(sender.Id, recipientId);
            if (!conversations.TryGetValue(key, out var messages))
            {
                messages = [];
                conversations.Add(key, messages);
            }
            messages.Add(new ChatMessage(sender.Id, sender.Name, text, DateTimeOffset.UtcNow));
            if (messages.Count > HistoryLimit)
            {
                messages.RemoveAt(0);
            }
        }

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
