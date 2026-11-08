namespace PefiChat.Profiles;

public interface IProfilePictureStore
{
    Task<(string? Name, string? Error)> UploadAsync(byte[] bytes, CancellationToken cancellationToken);
    Task<byte[]?> ReadAsync(string name, CancellationToken cancellationToken);
    Task DeleteAsync(string name, CancellationToken cancellationToken);
}
