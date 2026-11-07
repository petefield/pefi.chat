using System.Security.Claims;

namespace PefiChat.Chat;

public static class ChatIdentity
{
    public static ChatUser FromPrincipal(ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ChatUser.IdClaim)
            ?? throw new InvalidOperationException("The signed-in account has no chat identity.");
        var name = principal.Identity?.Name;
        return new ChatUser(id, string.IsNullOrWhiteSpace(name) ? "Unnamed user" : name);
    }
}
