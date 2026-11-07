using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace PefiChat.Chat;

[Authorize]
public sealed class ChatHub(ChatStore store, IHubContext<ChatHub> hub) : Hub
{
    private ChatUser CurrentUser => ChatIdentity.FromPrincipal(Context.User!);

    public override async Task OnConnectedAsync()
    {
        var connectionId = Context.ConnectionId;
        await store.ConnectAsync(connectionId, CurrentUser,
            () => hub.Clients.Client(connectionId).SendAsync("Changed"));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await store.DisconnectAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public ChatSnapshot GetSnapshot(string? otherUserId)
    {
        var user = CurrentUser;
        return new ChatSnapshot(user, store.OnlineUsers(user.Id), store.Messages(user.Id, otherUserId));
    }

    public Task<string?> SendMessage(string? recipientId, string? text) =>
        store.SendAsync(CurrentUser, recipientId, text);
}
