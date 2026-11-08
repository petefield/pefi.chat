using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using SkiaSharp;
using PefiChat.Chat;
using PefiChat.Profiles;

namespace PefiChat.Infrastructure.Profiles;

public sealed class BlobProfilePictureStore(BlobContainerClient container, ILogger<BlobProfilePictureStore> logger) : IProfilePictureStore
{
    public async Task<(string? Name, string? Error)> UploadAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        if (bytes.Length > ChatLimits.MaximumPictureBytes)
            return (null, "Pictures must be 512 KB or smaller.");

        using var input = new MemoryStream(bytes);
        using var codec = SKCodec.Create(input);
        if (codec is null) return (null, "Choose a valid image file.");
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || info.Width > 4096 || info.Height > 4096 ||
            (long)info.Width * info.Height > 16_000_000)
            return (null, "Pictures must be no larger than 4096 pixels per side and 16 megapixels.");
        using var decoded = SKBitmap.Decode(codec);
        if (decoded is null) return (null, "The image file is damaged or invalid.");
        using var thumbnail = new SKBitmap(256, 256);
        using var canvas = new SKCanvas(thumbnail);
        var side = Math.Min(info.Width, info.Height);
        var left = (info.Width - side) / 2f;
        var top = (info.Height - side) / 2f;
        canvas.DrawBitmap(decoded, new SKRect(left, top, left + side, top + side),
            new SKRect(0, 0, 256, 256), new SKSamplingOptions(SKFilterMode.Linear));
        using var image = SKImage.FromBitmap(thumbnail);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        var processed = encoded.ToArray();

        var name = Guid.NewGuid().ToString("N");
        await container.GetBlobClient(name).UploadAsync(BinaryData.FromBytes(processed),
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "image/png" } }, cancellationToken);
        return (name, null);
    }

    public async Task<byte[]?> ReadAsync(string name, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(name, "N", out _)) return null;
        try
        {
            return (await container.GetBlobClient(name).DownloadContentAsync(cancellationToken)).Value.Content.ToArray();
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            await container.GetBlobClient(name).DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException exception)
        {
            logger.LogWarning(exception, "Could not delete an unused profile picture {Picture}.", name);
        }
    }
}
