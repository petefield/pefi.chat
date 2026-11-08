using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PefiChat.Chat;
using Azure.Storage.Blobs;
using PefiChat.Infrastructure.Profiles;
using PefiChat.Profiles;
using PefiChat.Realtime;
using PefiChat.Push;

internal static class HubChecks
{
    public static async Task RunAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<IChatRepository, InMemoryChatRepository>();
        builder.Services.AddSingleton<IPushService, TestPushService>();
        builder.Services.AddSingleton<ChatService>();
        builder.Services.AddSingleton(new BlobContainerClient(new Uri("http://127.0.0.1:10000/profiles")));
        builder.Services.AddSingleton<IProfilePictureStore, BlobProfilePictureStore>();
        builder.Services.AddSingleton<ProfileService>();
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHub<ChatHub>("/chat-hub");
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var http = new HttpClient();
        var response = await http.PostAsync($"{address}/chat-hub/negotiate?negotiateVersion=1", null);
        Check(response.StatusCode == HttpStatusCode.Unauthorized, "hub rejects unauthenticated clients");

        await using var alice = CreateConnection(address, "alice");
        await using var bob = CreateConnection(address, "bob");
        await using var charlie = CreateConnection(address, "charlie");
        var aliceNotifications = 0;
        var bobNotifications = 0;
        alice.On("Changed", () => Interlocked.Increment(ref aliceNotifications));
        bob.On("Changed", () => Interlocked.Increment(ref bobNotifications));
        await alice.StartAsync();
        await bob.StartAsync();
        await charlie.StartAsync();
        var aliceState = await alice.InvokeAsync<ChatSnapshot>("GetSnapshot", (string?)null);
        Check(aliceState.User.Id == "alice" && aliceState.Users.Count == 2 && aliceState.Users.All(user => user.IsOnline), "hub derives identity and presence from authenticated connections");
        Check(await alice.InvokeAsync<string?>("SendMessage", (string?)null, "hello") is null, "hub accepts shared message");
        Check((await bob.InvokeAsync<ChatSnapshot>("GetSnapshot", (string?)null)).Messages.Single().Text == "hello", "shared message reaches another client");
        Check(await alice.InvokeAsync<string?>("UpdateProfile", "Alice_New", (byte[]?)null) is null, "hub accepts a valid username");
        Check((await bob.InvokeAsync<ChatSnapshot>("GetSnapshot", (string?)null)).Messages.Single().SenderName == "Alice_New", "hub returns current names on old messages");
        Check(await bob.InvokeAsync<string?>("UpdateProfile", "alice_new", (byte[]?)null) is not null, "hub rejects duplicate username");
        Check(await bob.InvokeAsync<string?>("UpdateProfile", "x", (byte[]?)null) is not null, "hub rejects invalid username");
        Check(await bob.InvokeAsync<string?>("UpdateProfile", "Bob_123", new byte[] { 1, 2, 3 }) is not null, "hub rejects invalid image content");
        Check(await alice.InvokeAsync<string?>("SendMessage", "bob", "secret") is null, "hub accepts direct message");
        Check((await bob.InvokeAsync<ChatSnapshot>("GetSnapshot", "alice")).Messages.Single().Text == "secret", "recipient reads direct message");
        Check((await charlie.InvokeAsync<ChatSnapshot>("GetSnapshot", "alice")).Messages.Count == 0, "third party cannot read another pair's messages");
        await WaitUntilAsync(() => Volatile.Read(ref aliceNotifications) > 0 && Volatile.Read(ref bobNotifications) > 0);
        Check(true, "connected clients receive change notifications");
        await bob.StopAsync();
        Check(await alice.InvokeAsync<string?>("SendMessage", "bob", "offline") is null, "hub accepts messages after recipient disconnects");
        await bob.StartAsync();
        Check((await bob.InvokeAsync<ChatSnapshot>("GetSnapshot", "alice")).Messages.Last().Text == "offline", "reconnected client receives offline messages");
        await app.StopAsync();
    }

    private static HubConnection CreateConnection(string address, string user) =>
        new HubConnectionBuilder().WithUrl($"{address}/chat-hub", options => options.Headers["X-Test-User"] = user).Build();

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(20, timeout.Token);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception($"FAILED: {description}");
        Console.WriteLine($"PASS: {description}");
    }
}

internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var id = Request.Headers["X-Test-User"].ToString();
        if (string.IsNullOrEmpty(id)) return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity([new Claim(ChatUser.IdClaim, id), new Claim(ClaimTypes.Name, id)], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
