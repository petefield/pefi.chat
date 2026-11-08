using PefiChat.Chat;

namespace PefiChat.Profiles;

public sealed class ProfileService(ChatService chat, IProfilePictureStore pictures)
{
    public async Task<string?> UpdateAsync(string userId, string? username, byte[]? image, CancellationToken cancellationToken)
    {
        username = username?.Trim();
        if (username is null || username.Length is < 3 or > ChatLimits.MaximumUsernameLength ||
            username.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '-'))
            return "Use 3–32 letters, numbers, underscores or hyphens for your username.";

        string? picture = null;
        if (image is { Length: > 0 })
        {
            var upload = await pictures.UploadAsync(image, cancellationToken);
            if (upload.Error is not null) return upload.Error;
            picture = upload.Name;
        }

        ProfileUpdateResult result;
        try
        {
            result = await chat.UpdateProfileAsync(userId, username, picture);
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
        await chat.ProfileChangedAsync();
        if (picture is not null && result.PreviousUser?.Picture is { } previous)
            await pictures.DeleteAsync(previous, CancellationToken.None);
        return null;
    }
}
