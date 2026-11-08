using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using PefiChat.Authentication;

namespace PefiChat.Endpoints;

public static class AuthenticationEndpoints
{
    public static void MapAuthenticationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/login/{provider}", (string provider, string? returnUrl, SignInProviders providers) =>
        {
            var scheme = provider.ToLowerInvariant() switch
            {
                "google" when providers.Google => "Google",
                "microsoft" when providers.Microsoft => "Microsoft",
                _ => null
            };
            return scheme is null
                ? Results.NotFound()
                : Results.Challenge(new AuthenticationProperties { RedirectUri = SafeReturnUrl(returnUrl) }, [scheme]);
        });

        endpoints.MapPost("/logout", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context))
                return Results.BadRequest();
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.LocalRedirect("/");
        }).RequireAuthorization();
    }

    private static string SafeReturnUrl(string? url)
    {
        const string prefix = "/chat/";
        if (url is null || !url.StartsWith(prefix, StringComparison.Ordinal)) return "/";
        var id = url[prefix.Length..];
        return id.Length is > 0 and <= 128 && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            ? url : "/";
    }
}
