namespace PefiChat.Chat;

public sealed record ChatUser(string Id, string Name)
{
    public const string IdClaim = "pefi:chat-id";
}

public sealed record ChatMessage(string SenderId, string SenderName, string Text, DateTimeOffset SentAt);

public sealed record ChatSnapshot(ChatUser User, IReadOnlyList<ChatUser> OnlineUsers, IReadOnlyList<ChatMessage> Messages);

public static class ChatLimits
{
    public const int MaximumMessageLength = 2000;
}
