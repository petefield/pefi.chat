namespace PefiChat.Chat;

public sealed record ChatUser(string Id, string Name, string? Picture = null, string? Username = null)
{
    public const string IdClaim = "pefi:chat-id";
}

public sealed record ChatMessage(string SenderId, string SenderName, string Text, DateTimeOffset SentAt, string? SenderPicture = null);

public sealed record ChatContact(string Id, string Name, bool IsOnline, string? Picture = null);

public sealed record ChatSnapshot(ChatUser User, IReadOnlyList<ChatContact> Users, IReadOnlyList<ChatMessage> Messages);

public static class ChatLimits
{
    public const int MaximumMessageLength = 2000;
    public const int MaximumPictureBytes = 512 * 1024;
    public const int MaximumUsernameLength = 32;
}
