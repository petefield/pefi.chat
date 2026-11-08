namespace PefiChat.Chat;

public interface IChatRepository
{
    Task SaveUserAsync(ChatUser user);
    Task<ChatUser?> GetUserAsync(string id);
    Task<ProfileUpdateResult> UpdateProfileAsync(string id, string username, string? picture);
    Task<IReadOnlyList<ChatUser>> GetUsersAsync();
    Task<bool> UserExistsAsync(string id);
    Task SaveMessageAsync(string conversationId, ChatMessage message);
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string conversationId);
}

public sealed record ProfileUpdateResult(bool Success, string? Error, ChatUser? PreviousUser = null);
