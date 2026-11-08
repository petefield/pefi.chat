using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using PefiChat.Chat;

namespace PefiChat.Authentication;

public static class ChatIdentity
{
    public static void AddToIdentity(ClaimsIdentity identity, string provider)
    {
        var subject = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new InvalidOperationException($"{provider} did not provide an account identifier.");
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{provider}:{subject}"));
        identity.AddClaim(new Claim(ChatUser.IdClaim, Convert.ToHexString(bytes)));
    }

    public static ChatUser FromPrincipal(ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ChatUser.IdClaim)
            ?? throw new InvalidOperationException("The signed-in account has no chat identity.");
        var name = principal.Identity?.Name;
        return new ChatUser(id, string.IsNullOrWhiteSpace(name) ? "Unnamed user" : name);
    }
}
