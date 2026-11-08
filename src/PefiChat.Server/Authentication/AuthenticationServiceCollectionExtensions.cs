using Microsoft.AspNetCore.Authentication.Cookies;

namespace PefiChat.Authentication;

public static class AuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddChatAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var authentication = services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Events.OnRedirectToLogin = context =>
                    RedirectOrSetStatusAsync(context, StatusCodes.Status401Unauthorized);
                options.Events.OnRedirectToAccessDenied = context =>
                    RedirectOrSetStatusAsync(context, StatusCodes.Status403Forbidden);
            });

        var googleConfigured = IsConfigured(configuration, "Google");
        var microsoftConfigured = IsConfigured(configuration, "Microsoft");

        if (googleConfigured)
        {
            authentication.AddGoogle(options =>
            {
                options.ClientId = configuration["Authentication:Google:ClientId"]!;
                options.ClientSecret = configuration["Authentication:Google:ClientSecret"]!;
                options.Events.OnCreatingTicket = context =>
                {
                    ChatIdentity.AddToIdentity(context.Identity!, "Google");
                    return Task.CompletedTask;
                };
            });
        }

        if (microsoftConfigured)
        {
            authentication.AddMicrosoftAccount(options =>
            {
                options.ClientId = configuration["Authentication:Microsoft:ClientId"]!;
                options.ClientSecret = configuration["Authentication:Microsoft:ClientSecret"]!;
                options.Events.OnCreatingTicket = context =>
                {
                    ChatIdentity.AddToIdentity(context.Identity!, "Microsoft");
                    return Task.CompletedTask;
                };
            });
        }

        services.AddSingleton(new SignInProviders(googleConfigured, microsoftConfigured));
        services.AddAuthorization();
        return services;
    }

    private static bool IsConfigured(IConfiguration configuration, string provider) =>
        !string.IsNullOrWhiteSpace(configuration[$"Authentication:{provider}:ClientId"]) &&
        !string.IsNullOrWhiteSpace(configuration[$"Authentication:{provider}:ClientSecret"]);

    private static Task RedirectOrSetStatusAsync(
        Microsoft.AspNetCore.Authentication.RedirectContext<CookieAuthenticationOptions> context, int statusCode)
    {
        if (context.Request.Path.StartsWithSegments("/chat-hub") ||
            context.Request.Path.StartsWithSegments("/profile-pictures"))
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentLength = 0;
        }
        else
        {
            context.Response.Redirect(context.RedirectUri);
        }
        return Task.CompletedTask;
    }
}
