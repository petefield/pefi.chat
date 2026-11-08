using PefiChat.Profiles;

namespace PefiChat.Endpoints;

public static class ProfilePictureEndpoints
{
    public static void MapProfilePictureEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/profile-pictures/{name}", async (string name, IProfilePictureStore pictures, HttpContext context, CancellationToken cancellationToken) =>
        {
            var content = await pictures.ReadAsync(name, cancellationToken);
            if (content is null) return Results.NotFound();
            context.Response.Headers.CacheControl = "private, max-age=3600";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(content, "image/png");
        }).RequireAuthorization();
    }
}
