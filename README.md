# Pefi Chat

A small chat with a Blazor WebAssembly client, a .NET server, a shared room, and direct messages. The browser connects to an authenticated SignalR hub for real-time updates; sign-in and chat state remain on the server.

## Run locally

Requires the .NET 10 SDK and a current, security-patched ASP.NET Core 10 runtime. Create OAuth applications with Google and/or Microsoft and register these redirect URIs:

| Provider | Redirect URI |
| --- | --- |
| Google | `https://localhost:7268/signin-google` |
| Microsoft | `https://localhost:7268/signin-microsoft` |

Use the Microsoft identity platform's **Web** redirect URI type and enable the account types you want to allow. For Google, configure the OAuth consent screen and add any required test users. These addresses match the included HTTPS launch profile; use your actual public HTTPS origin when deploying.

Keep credentials out of source control. For local development, configure either or both providers with .NET user secrets:

```sh
dotnet user-secrets set "Authentication:Google:ClientId" "YOUR_CLIENT_ID"
dotnet user-secrets set "Authentication:Google:ClientSecret" "YOUR_CLIENT_SECRET"
dotnet user-secrets set "Authentication:Microsoft:ClientId" "YOUR_CLIENT_ID"
dotnet user-secrets set "Authentication:Microsoft:ClientSecret" "YOUR_CLIENT_SECRET"
dotnet dev-certs https --trust
dotnet run --launch-profile https
```

Visit `https://localhost:7268`. A provider appears on the sign-in screen only when both of its credentials are configured. For deployment, supply the same configuration keys through a secret manager or environment variables such as `Authentication__Google__ClientId`. Configure HTTPS and protect ASP.NET Core data-protection keys appropriately for the hosting environment.

Run the chat behavior and SignalR integration checks with `dotnet run --project tests/PefiChat.Tests`.

## Deploy to Azure App Service

The [GitHub Actions workflow](.github/workflows/azure-app-service.yml) runs the chat checks, publishes the server and WebAssembly client together, and deploys to an existing App Service on pushes to `main` or manual runs on `main`. Runs manually started from another branch build and test but do not deploy. Deployment targets the GitHub `production` environment and uses short-lived Azure credentials through OpenID Connect (OIDC); it does not provision Azure resources.

1. Create a **Linux** Azure App Service using the built-in .NET 10 runtime stack (`DOTNETCORE|10.0`). Keep the runtime patched, enable HTTPS-only and WebSockets, and use a single instance: chat state is local to one process. The workflow deploys a framework-dependent publish and sets the Linux startup command to `dotnet PefiChat.dll`.
2. In App Service application settings, set `ASPNETCORE_FORWARDEDHEADERS_ENABLED` to `true`. Linux App Service terminates TLS at a proxy; this setting lets ASP.NET Core use the forwarded scheme for HTTPS redirects and OAuth callbacks. It enables forwarded headers without restricting trusted proxies: only use it where requests to the app are isolated behind trusted, header-normalizing ingress. Review [Microsoft's proxy guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0), especially if adding another proxy or allowing direct access.
3. Register the public HTTPS callback URLs in each enabled OAuth provider: `https://<your-app-host>/signin-google` and `https://<your-app-host>/signin-microsoft`. In App Service application settings, set both `Authentication__Google__ClientId` and `Authentication__Google__ClientSecret`, and/or both `Authentication__Microsoft__ClientId` and `Authentication__Microsoft__ClientSecret`. Use App Service secrets or Key Vault references; never put client secrets in the repository or workflow.
4. Create an Azure Entra application/service principal and an OIDC federated credential for GitHub Actions. Its issuer is `https://token.actions.githubusercontent.com` and audience is `api://AzureADTokenExchange`. Match the subject to the token GitHub issues: repositories using immutable subject claims use `repo:<OWNER>@<OWNER_ID>/<REPOSITORY>@<REPOSITORY_ID>:environment:production`; older repositories may use `repo:<OWNER>/<REPOSITORY>:environment:production`. Grant the service principal a role that permits App Service deployment, such as **Website Contributor**, scoped to the target web app. See [GitHub's OIDC guidance](https://docs.github.com/en/actions/reference/security/oidc) and [Microsoft's setup guide](https://learn.microsoft.com/en-us/azure/developer/github/connect-from-azure-openid-connect).
5. Create the GitHub environment named `production`. Set its deployment branch policy to `main` and optionally require approval. Add these environment secrets: `AZURE_CLIENT_ID` (application/client ID), `AZURE_TENANT_ID` (directory/tenant ID), and `AZURE_SUBSCRIPTION_ID`. Add the environment variable `AZURE_WEBAPP_NAME` with the App Service name (not its URL).
6. Push to `main` or run **Deploy to Azure App Service** manually on `main`. Verify the site loads over HTTPS and test both configured sign-in providers and real-time chat after deployment.

App Service persists the ASP.NET Core data-protection key ring under its home directory by default, but those keys are **not encrypted at rest**. Configure a protected external key store if required; see [Microsoft's data-protection guidance](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/default-settings). Deployments and restarts clear all chat messages and presence.

## Behavior and limitations

- The online list contains users with a connected SignalR client. Multiple tabs count as one user. Only online users can be selected as direct-message recipients. A lost connection is automatically retried, with a manual reconnect option if retries fail.
- Google and Microsoft identities are separate, even when they use the same email address.
- Messages are held only in server memory, limited to the most recent 200 per conversation, and are lost on restart. Direct messages are visible only to their two participants. Do not use this app for sensitive information.
- This is a single-server design: presence and messages are not shared across instances. It has no moderation, durable storage, or delivery guarantees.
