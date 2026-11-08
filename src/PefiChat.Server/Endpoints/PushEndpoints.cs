using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using PefiChat.Authentication;
using PefiChat.Push;

namespace PefiChat.Endpoints;

/// <summary>Browser push subscription keys.</summary>
public sealed record PushKeysRequest(string? P256dh, string? Auth);
/// <summary>A browser push subscription.</summary>
public sealed record SubscribePushRequest(string? Endpoint, PushKeysRequest? Keys);
/// <summary>A server-authorized notification to a user.</summary>
public sealed record SendPushRequest(string? UserId, PushPayload? Payload);

public static class PushEndpoints
{
    public static void MapPushEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/push/public-key", (IPushService push) =>
            string.IsNullOrWhiteSpace(push.PublicKey)
                ? Results.Problem("Web Push is not configured.", statusCode: 503)
                : Results.Ok(new { publicKey = push.PublicKey }))
            .RequireAuthorization();

        endpoints.MapPost("/api/push/subscribe", async (SubscribePushRequest request, HttpContext context,
            IAntiforgery antiforgery, IPushService push, CancellationToken cancellationToken) =>
        {
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { return Results.BadRequest("Invalid antiforgery token."); }
            if (!ValidEndpoint(request.Endpoint) || !ValidKey(request.Keys?.P256dh, 65) ||
                !ValidKey(request.Keys?.Auth, 16))
                return Results.BadRequest("Invalid push subscription.");
            await push.SubscribeAsync(ChatIdentity.FromPrincipal(context.User).Id,
                new BrowserSubscription(request.Endpoint!, request.Keys!.P256dh!, request.Keys.Auth!), cancellationToken);
            return Results.NoContent();
        }).RequireAuthorization();

        endpoints.MapPost("/api/push/send", async (SendPushRequest request, HttpContext context,
            IConfiguration configuration, IPushService push, CancellationToken cancellationToken) =>
        {
            var secret = configuration["Push:SendSecret"];
            if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
                return Results.Problem("Push delivery API is not configured.", statusCode: 503);
            var supplied = context.Request.Headers["X-Push-Secret"].ToString();
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(secret)),
                SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
                return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(request.UserId) || request.UserId.Length > 128 ||
                request.Payload is not { } payload || string.IsNullOrWhiteSpace(payload.Title) ||
                payload.Title.Length > 256 || payload.Body is null || payload.Body.Length > 200 ||
                payload.Icon != "/icon-512.png" || string.IsNullOrWhiteSpace(payload.Tag) || payload.Tag.Length > 256 ||
                string.IsNullOrWhiteSpace(payload.Url) || !payload.Url.StartsWith("/chat/", StringComparison.Ordinal) ||
                payload.Url.Contains('\\') || payload.Url.Contains("//", StringComparison.Ordinal))
                return Results.BadRequest("Invalid notification.");
            await push.SendAsync(request.UserId, payload, cancellationToken);
            return Results.NoContent();
        });

        endpoints.MapGet("/icon-512.png", () => Results.Redirect("/icons/icon-512.png"));
    }

    private static bool ValidEndpoint(string? endpoint)
    {
        if (endpoint is null || endpoint.Length > 2048 || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo))
            return false;
        var host = uri.IdnHost;
        return host.Equals("fcm.googleapis.com", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("updates.push.services.mozilla.com", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("web.push.apple.com", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".push.apple.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ValidKey(string? value, int length)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128) return false;
        try
        {
            var bytes = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(value);
            if (bytes.Length != length || (length == 65 && bytes[0] != 4)) return false;
            if (length == 65)
            {
                using var key = ECDiffieHellman.Create(new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint { X = bytes[1..33], Y = bytes[33..65] }
                });
            }
            return true;
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or ArgumentException) { return false; }
    }
}
