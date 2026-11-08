using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PefiChat.Authentication;
using PefiChat.Chat;
using PefiChat.Profiles;

namespace PefiChat.Realtime;

[Authorize]
public sealed class ChatHub(ChatService chat, IHubContext<ChatHub> hub, ProfileService profiles) : Hub
{
    private ChatUser CurrentUser => ChatIdentity.FromPrincipal(Context.User!);

    public override async Task OnConnectedAsync()
    {
        var connectionId = Context.ConnectionId;
        await chat.ConnectAsync(connectionId, CurrentUser,
            () => hub.Clients.Client(connectionId).SendAsync("Changed"));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await chat.DisconnectAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task<ChatSnapshot> GetSnapshot(string? otherUserId)
    {
        var user = CurrentUser;
        var profile = await chat.GetUserAsync(user.Id) ?? user;
        return new ChatSnapshot(profile, await chat.GetUsersAsync(user.Id), await chat.GetMessagesAsync(user.Id, otherUserId));
    }

    public Task<string?> SendMessage(string? recipientId, string? text) =>
        chat.SendMessageAsync(CurrentUser, recipientId, text);

    public Task<string?> UpdateProfile(string? username, byte[]? image) =>
        profiles.UpdateAsync(CurrentUser.Id, username, image, Context.ConnectionAborted);
}
