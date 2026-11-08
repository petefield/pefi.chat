using PefiChat.Authentication;
using PefiChat.Chat;
using PefiChat.Components;
using PefiChat.Endpoints;
using PefiChat.Infrastructure;
using PefiChat.Profiles;
using PefiChat.Realtime;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveWebAssemblyComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSignalR(options => options.MaximumReceiveMessageSize = 768 * 1024);
builder.Services.AddChatAuthentication(builder.Configuration);
builder.Services.AddChatInfrastructure();
builder.Services.AddSingleton<ChatService>();
builder.Services.AddSingleton<ProfileService>();

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

app.MapAuthenticationEndpoints();
app.MapProfilePictureEndpoints();
app.MapStaticAssets();
app.MapHub<ChatHub>("/chat-hub");

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(PefiChat.Client.ChatRoom).Assembly);

app.Run();
