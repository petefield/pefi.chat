using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;

internal static class WebPushChecks
{
    public static async Task RunAsync()
    {
        using var vapidKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = vapidKey.ExportParameters(true);
        byte[] vapidPublic = [4, .. parameters.Q.X!, .. parameters.Q.Y!];
        using var authentication = new VapidAuthentication(WebEncoders.Base64UrlEncode(vapidPublic),
            WebEncoders.Base64UrlEncode(parameters.D!)) { Subject = "mailto:push@example.com" };
        using var receiver = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var receiverKey = receiver.ExportParameters(false).Q;
        byte[] publicKey = [4, .. receiverKey.X!, .. receiverKey.Y!];
        var subscription = new PushSubscription { Endpoint = "https://fcm.googleapis.com/test" };
        subscription.SetKey(PushEncryptionKeyName.P256DH, WebEncoders.Base64UrlEncode(publicKey));
        subscription.SetKey(PushEncryptionKeyName.Auth, WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16)));
        var handler = new CaptureHandler();
        using var http = new HttpClient(handler);
        var client = new PushServiceClient(http);
        const string payload = "{\"title\":\"Secret message\"}";
        await client.RequestPushMessageDeliveryAsync(subscription, new PushMessage(payload), authentication, CancellationToken.None);
        if (handler.Request is null || handler.Body is null || handler.Request.Method != HttpMethod.Post ||
            handler.Request.RequestUri?.Host != "fcm.googleapis.com")
            throw new Exception("Web Push did not post to the push service.");
        if (!handler.Request.Headers.TryGetValues("Authorization", out var headers) ||
            !headers.Single().StartsWith("vapid ", StringComparison.OrdinalIgnoreCase))
            throw new Exception($"Web Push authorization scheme: {handler.Request.Headers.Authorization?.Scheme ?? "(none)"}.");
        var parts = headers.Single()["vapid ".Length..].Split(',');
        var jwt = parts.Single(part => part.TrimStart().StartsWith("t=", StringComparison.Ordinal)).Trim()[2..];
        var jwtParts = jwt.Split('.');
        using var claims = JsonDocument.Parse(WebEncoders.Base64UrlDecode(jwtParts[1]));
        if (jwtParts.Length != 3 || claims.RootElement.GetProperty("aud").GetString() != "https://fcm.googleapis.com" ||
            claims.RootElement.GetProperty("sub").GetString() != "mailto:push@example.com" ||
            !vapidKey.VerifyData(Encoding.ASCII.GetBytes(jwtParts[0] + "." + jwtParts[1]),
                WebEncoders.Base64UrlDecode(jwtParts[2]), HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            throw new Exception("VAPID JWT audience, subject or signature is invalid.");
        if (!handler.Request.Content!.Headers.ContentEncoding.Contains("aes128gcm"))
            throw new Exception($"Unexpected Web Push content encoding: {string.Join(",", handler.Request.Content.Headers.ContentEncoding)}");
        if (Encoding.UTF8.GetString(handler.Body).Contains(payload, StringComparison.Ordinal))
            throw new Exception("Web Push sent plaintext.");
        Console.WriteLine("PASS: WebPush encrypts the payload and sends VAPID authorization to the push service");
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public byte[]? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Created);
        }
    }
}
