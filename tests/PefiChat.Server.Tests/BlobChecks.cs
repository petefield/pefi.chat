using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging.Abstractions;
using PefiChat.Chat;
using PefiChat.Infrastructure.Profiles;
using SkiaSharp;

internal static class BlobChecks
{
    public static async Task RunAsync(string endpoint)
    {
        var container = new BlobContainerClient(new Uri(endpoint), new AzureCliCredential());
        var pictures = new BlobProfilePictureStore(container, NullLogger<BlobProfilePictureStore>.Instance);
        using var bitmap = new SKBitmap(300, 150);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        var uploaded = await pictures.UploadAsync(encoded.ToArray(), CancellationToken.None);
        if (uploaded.Name is null) throw new Exception($"Blob upload failed: {uploaded.Error}");
        try
        {
            var data = await pictures.ReadAsync(uploaded.Name, CancellationToken.None)
                ?? throw new Exception("Blob download failed.");
            using var result = SKBitmap.Decode(data);
            if (result is null || result.Width != 256 || result.Height != 256)
                throw new Exception("Blob image was not resized to 256x256.");
            if ((await pictures.UploadAsync(new byte[ChatLimits.MaximumPictureBytes + 1], CancellationToken.None)).Error is null)
                throw new Exception("Oversized upload was accepted.");
            if (await pictures.ReadAsync("invalid-path", CancellationToken.None) is not null)
                throw new Exception("Invalid blob name was accepted.");
            Console.WriteLine("PASS: private Blob profile image upload, resize, download, and validation");
        }
        finally
        {
            await pictures.DeleteAsync(uploaded.Name, CancellationToken.None);
        }
        if (await pictures.ReadAsync(uploaded.Name, CancellationToken.None) is not null)
            throw new Exception("Blob deletion failed.");
    }
}
