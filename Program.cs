using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using PefiChat.Chat;
using PefiChat.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveWebAssemblyComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();
builder.Services.AddSignalR();
builder.Services.AddSingleton<ChatStore>();

var authentication = builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/chat-hub"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentLength = 0;
            }
            else
            {
                context.Response.Redirect(context.RedirectUri);
            }
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/chat-hub"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentLength = 0;
            }
            else
            {
                context.Response.Redirect(context.RedirectUri);
            }
            return Task.CompletedTask;
        };
    });

var googleConfigured = IsConfigured(builder.Configuration, "Google");
var microsoftConfigured = IsConfigured(builder.Configuration, "Microsoft");

if (googleConfigured)
{
    authentication.AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Authentication:Google:ClientId"]!;
        options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;
        options.Events.OnCreatingTicket = context =>
        {
            AddChatIdentity(context.Identity!, "Google");
            return Task.CompletedTask;
        };
    });
}

if (microsoftConfigured)
{
    authentication.AddMicrosoftAccount(options =>
    {
        options.ClientId = builder.Configuration["Authentication:Microsoft:ClientId"]!;
        options.ClientSecret = builder.Configuration["Authentication:Microsoft:ClientSecret"]!;
        options.Events.OnCreatingTicket = context =>
        {
            AddChatIdentity(context.Identity!, "Microsoft");
            return Task.CompletedTask;
        };
    });
}

builder.Services.AddSingleton(new SignInProviders(googleConfigured, microsoftConfigured));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/login/{provider}", (string provider) =>
{
    var scheme = provider.ToLowerInvariant() switch
    {
        "google" when googleConfigured => "Google",
        "microsoft" when microsoftConfigured => "Microsoft",
        _ => null
    };
    return scheme is null
        ? Results.NotFound()
        : Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }, [scheme]);
});

app.MapPost("/logout", async (HttpContext context, IAntiforgery antiforgery) =>
{
    if (!await antiforgery.IsRequestValidAsync(context))
    {
        return Results.BadRequest();
    }
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("/");
}).RequireAuthorization();

app.MapStaticAssets();
app.MapHub<ChatHub>("/chat-hub");
app.MapRazorComponents<App>().AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(PefiChat.Client.ChatRoom).Assembly);
app.Run();

static bool IsConfigured(IConfiguration configuration, string provider) =>
    !string.IsNullOrWhiteSpace(configuration[$"Authentication:{provider}:ClientId"]) &&
    !string.IsNullOrWhiteSpace(configuration[$"Authentication:{provider}:ClientSecret"]);

static void AddChatIdentity(ClaimsIdentity identity, string provider)
{
    var subject = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? throw new InvalidOperationException($"{provider} did not provide an account identifier.");
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{provider}:{subject}"));
    identity.AddClaim(new Claim(ChatUser.IdClaim, Convert.ToHexString(bytes)));
}

public sealed record SignInProviders(bool Google, bool Microsoft);
