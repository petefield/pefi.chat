using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PefiChat.Authentication;
using PefiChat.Chat;
using PefiChat.Endpoints;
using PefiChat.Profiles;

internal static class EndpointChecks
{
    public static async Task RunAsync()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "test-subject")]);
        ChatIdentity.AddToIdentity(identity, "Microsoft");
        Check(identity.FindFirst(ChatUser.IdClaim)?.Value == "34AECF4C980F9381201A90DC2194086E9708459EF4B66EA86DDBED6A750EC25F",
            "provider identities retain their stable chat ID");
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Microsoft:ClientId"] = "test-client",
            ["Authentication:Microsoft:ClientSecret"] = "test-secret",
            ["Authentication:Google:ClientId"] = "incomplete-client"
        });
        builder.Services.AddChatAuthentication(builder.Configuration);
        builder.Services.AddAntiforgery();
        builder.Services.AddSingleton<IProfilePictureStore, TestPictureStore>();

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        app.MapAuthenticationEndpoints();
        app.MapProfilePictureEndpoints();
        app.MapPost("/chat-hub/negotiate", () => Results.Ok()).RequireAuthorization();
        app.MapGet("/test-sign-in", async (HttpContext context) =>
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "test")], CookieAuthenticationDefaults.AuthenticationScheme));
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
            return Results.Ok();
        });
        app.MapGet("/test-token", (HttpContext context, IAntiforgery antiforgery) =>
            antiforgery.GetAndStoreTokens(context).RequestToken).RequireAuthorization();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(address) };

        Check((await client.GetAsync("/login/google")).StatusCode == HttpStatusCode.NotFound, "incomplete provider configuration is not enabled");
        Check((await client.GetAsync("/login/unknown")).StatusCode == HttpStatusCode.NotFound, "unknown sign-in provider is rejected");
        var challenge = await client.GetAsync("/login/MICROSOFT");
        Check(challenge.StatusCode == HttpStatusCode.Redirect &&
            challenge.Headers.Location?.Host == "login.microsoftonline.com", "configured provider issues an OAuth challenge");
        Check((await client.GetAsync("/profile-pictures/example")).StatusCode == HttpStatusCode.Unauthorized,
            "profile pictures require authentication without redirecting");
        Check((await client.PostAsync("/chat-hub/negotiate", null)).StatusCode == HttpStatusCode.Unauthorized,
            "hub authentication failures do not redirect");
        Check((await client.PostAsync("/logout", null)).StatusCode == HttpStatusCode.Redirect,
            "anonymous logout redirects to sign in");

        await client.GetAsync("/test-sign-in");
        var picture = await client.GetAsync("/profile-pictures/example");
        Check(picture.StatusCode == HttpStatusCode.OK && picture.Content.Headers.ContentType?.MediaType == "image/png" &&
            picture.Headers.CacheControl?.Private == true && picture.Headers.CacheControl.MaxAge == TimeSpan.FromHours(1) &&
            picture.Headers.GetValues("X-Content-Type-Options").Single() == "nosniff" &&
            (await picture.Content.ReadAsByteArrayAsync()).SequenceEqual(new byte[] { 1, 2, 3 }),
            "authenticated picture response preserves content and cache headers");
        Check((await client.GetAsync("/profile-pictures/missing")).StatusCode == HttpStatusCode.NotFound,
            "missing profile picture returns not found");
        Check((await client.PostAsync("/logout", new FormUrlEncodedContent([]))).StatusCode == HttpStatusCode.BadRequest,
            "logout rejects missing antiforgery token");
        var token = await client.GetStringAsync("/test-token");
        var logout = await client.PostAsync("/logout", new FormUrlEncodedContent(
            [new KeyValuePair<string, string>("__RequestVerificationToken", token)]));
        Check(logout.StatusCode == HttpStatusCode.Redirect && logout.Headers.Location?.OriginalString == "/",
            "logout accepts antiforgery token and redirects home");
        Check((await client.GetAsync("/profile-pictures/example")).StatusCode == HttpStatusCode.Unauthorized,
            "logout clears the authentication cookie");
        await app.StopAsync();
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception($"FAILED: {description}");
        Console.WriteLine($"PASS: {description}");
    }

    private sealed class TestPictureStore : IProfilePictureStore
    {
        public Task<(string? Name, string? Error)> UploadAsync(byte[] bytes, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<byte[]?> ReadAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult<byte[]?>(name == "example" ? [1, 2, 3] : null);
        public Task DeleteAsync(string name, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
