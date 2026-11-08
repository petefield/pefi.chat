using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace PefiChat.Chat;

[Authorize]
public sealed class ChatHub(ChatStore store, IHubContext<ChatHub> hub, ProfilePictures pictures) : Hub
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

    public async Task<ChatSnapshot> GetSnapshot(string? otherUserId)
    {
        var user = CurrentUser;
        var profile = await store.GetUserAsync(user.Id) ?? user;
        return new ChatSnapshot(profile, await store.UsersAsync(user.Id), await store.MessagesAsync(user.Id, otherUserId));
    }

    public Task<string?> SendMessage(string? recipientId, string? text) =>
        store.SendAsync(CurrentUser, recipientId, text);

    public async Task<string?> UpdateProfile(string? username, byte[]? image)
    {
        username = username?.Trim();
        if (username is null || username.Length is < 3 or > ChatLimits.MaximumUsernameLength ||
            username.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '-'))
            return "Use 3–32 letters, numbers, underscores or hyphens for your username.";
        string? picture = null;
        if (image is { Length: > 0 })
        {
            var upload = await pictures.UploadAsync(image, Context.ConnectionAborted);
            if (upload.Error is not null) return upload.Error;
            picture = upload.Name;
        }
        ProfileUpdateResult result;
        try
        {
            result = await store.UpdateProfileAsync(CurrentUser.Id, username, picture);
        }
        catch
        {
            if (picture is not null) await pictures.DeleteAsync(picture, CancellationToken.None);
            throw;
        }
        if (!result.Success)
        {
            if (picture is not null) await pictures.DeleteAsync(picture, CancellationToken.None);
            return result.Error;
        }
        await store.ProfileChangedAsync();
        if (picture is not null && result.PreviousUser?.Picture is { } previous)
            await pictures.DeleteAsync(previous, CancellationToken.None);
        return null;
    }
}
