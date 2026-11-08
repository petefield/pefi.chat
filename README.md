# Pefi Chat

A small chat with a Blazor WebAssembly client, a .NET server, a shared room, and direct messages. The browser connects to an authenticated SignalR hub for real-time updates; sign-in and chat state remain on the server.

## Run locally with Docker Compose

Requires Docker and the Docker Compose plugin. For Microsoft sign-in, create a **separate development** Microsoft Entra app registration with a Web redirect URI of `http://localhost:8080/signin-microsoft`. Enable the account types you want to allow and create a client secret. Keep this registration separate from production.

Create `.env.local` in the project directory with the development credentials (it is ignored by Git and excluded from the Docker build):

```text
Authentication__Microsoft__ClientId=YOUR_DEVELOPMENT_CLIENT_ID
Authentication__Microsoft__ClientSecret=YOUR_DEVELOPMENT_CLIENT_SECRET
Cosmos__Endpoint=https://YOUR_ACCOUNT.documents.azure.com:443/
Cosmos__Database=pefi-chat
Cosmos__Key=YOUR_COSMOS_ACCOUNT_KEY
Profiles__BlobEndpoint=https://YOUR_STORAGE_ACCOUNT.blob.core.windows.net/
Profiles__AccountName=YOUR_STORAGE_ACCOUNT
Profiles__Key=YOUR_STORAGE_ACCOUNT_KEY
```

Then run:

```sh
docker compose up --build -d
```

Open [http://localhost:8080](http://localhost:8080), using `localhost` rather than `127.0.0.1` so the OAuth redirect matches. Stop it with `docker compose down`. Cosmos and profile storage configuration are required; `.env.local` changes require `docker compose up -d` to recreate the container. Store the file securely: the account keys grant broad data access, and local use of shared Azure resources can change production data. Never use production OAuth credentials locally. The container runs the published app in the Production environment so its fingerprinted WebAssembly assets are served correctly; the HTTP listener is bound only to the local machine and is intended for development, not deployment. Messages survive container recreation; local sign-in cookies do not.

## Run locally with the .NET SDK

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
dotnet user-secrets set "Cosmos:Endpoint" "https://YOUR_ACCOUNT.documents.azure.com:443/"
dotnet user-secrets set "Cosmos:Database" "pefi-chat"
dotnet user-secrets set "Profiles:BlobEndpoint" "https://YOUR_STORAGE_ACCOUNT.blob.core.windows.net/"
az login
dotnet dev-certs https --trust
dotnet run --launch-profile https
```

Visit `https://localhost:7268`. A provider appears on the sign-in screen only when both of its credentials are configured. For deployment, supply the same configuration keys through a secret manager or environment variables such as `Authentication__Google__ClientId`. Configure HTTPS and protect ASP.NET Core data-protection keys appropriately for the hosting environment.

Run the chat behavior and SignalR integration checks with `dotnet run --project tests/PefiChat.Tests`.
To include the Cosmos round-trip check, run `COSMOS_TEST_ENDPOINT=https://YOUR_ACCOUNT.documents.azure.com:443/ dotnet run --project tests/PefiChat.Tests` after `az login`. This writes and removes test records in `pefi-chat`.
To include the Blob round-trip check, also set `BLOB_TEST_ENDPOINT=https://YOUR_STORAGE_ACCOUNT.blob.core.windows.net/profiles`; this writes and removes a test image.

## Deploy to Azure App Service

The [GitHub Actions workflow](.github/workflows/azure-app-service.yml) runs the chat checks, publishes the server and WebAssembly client together, and deploys to an existing App Service on pushes to `main` or manual runs on `main`. Runs manually started from another branch build and test but do not deploy. Deployment targets the GitHub `production` environment and uses short-lived Azure credentials through OpenID Connect (OIDC); it does not provision Azure resources.

1. Create a **Linux** Azure App Service using the built-in .NET 10 runtime stack (`DOTNETCORE|10.0`). Keep the runtime patched, enable HTTPS-only and WebSockets, and use a single instance: presence and live notifications are local to one process. The workflow deploys a framework-dependent publish and sets the Linux startup command to `dotnet PefiChat.dll`.
2. In App Service application settings, set `ASPNETCORE_FORWARDEDHEADERS_ENABLED` to `true`. Linux App Service terminates TLS at a proxy; this setting lets ASP.NET Core use the forwarded scheme for HTTPS redirects and OAuth callbacks. It enables forwarded headers without restricting trusted proxies: only use it where requests to the app are isolated behind trusted, header-normalizing ingress. Review [Microsoft's proxy guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0), especially if adding another proxy or allowing direct access.
3. Register the public HTTPS callback URLs in each enabled OAuth provider: `https://<your-app-host>/signin-google` and `https://<your-app-host>/signin-microsoft`. In App Service application settings, set both `Authentication__Google__ClientId` and `Authentication__Google__ClientSecret`, and/or both `Authentication__Microsoft__ClientId` and `Authentication__Microsoft__ClientSecret`. Use App Service secrets or Key Vault references; never put client secrets in the repository or workflow.
4. Create an Azure Entra application/service principal and an OIDC federated credential for GitHub Actions. Its issuer is `https://token.actions.githubusercontent.com` and audience is `api://AzureADTokenExchange`. Match the subject to the token GitHub issues: repositories using immutable subject claims use `repo:<OWNER>@<OWNER_ID>/<REPOSITORY>@<REPOSITORY_ID>:environment:production`; older repositories may use `repo:<OWNER>/<REPOSITORY>:environment:production`. Grant the service principal a role that permits App Service deployment, such as **Website Contributor**, scoped to the target web app. See [GitHub's OIDC guidance](https://docs.github.com/en/actions/reference/security/oidc) and [Microsoft's setup guide](https://learn.microsoft.com/en-us/azure/developer/github/connect-from-azure-openid-connect).
5. Create the GitHub environment named `production`. Set its deployment branch policy to `main` and optionally require approval. Add these environment secrets: `AZURE_CLIENT_ID` (application/client ID), `AZURE_TENANT_ID` (directory/tenant ID), and `AZURE_SUBSCRIPTION_ID`. Add the environment variable `AZURE_WEBAPP_NAME` with the App Service name (not its URL).
6. Push to `main` or run **Deploy to Azure App Service** manually on `main`. Verify the site loads over HTTPS and test both configured sign-in providers and real-time chat after deployment.

Provision an Azure Cosmos DB for NoSQL database named `pefi-chat` with `users` and `messages` containers, each partitioned by `/partitionKey`. Set `Cosmos__Endpoint` and `Cosmos__Database` in App Service, enable its system-assigned managed identity, and grant that identity the Cosmos DB Built-in Data Contributor role scoped to the database. For SDK development, grant the signed-in Azure CLI user the same Cosmos data-plane role; use `az login` before running the app. No Cosmos key is needed for either identity-based configuration. Cosmos usage incurs Azure charges.

Create a StorageV2 account with public blob access disabled and a private container named `profiles`. Set `Profiles__BlobEndpoint` to the account's Blob service URL and grant the App Service managed identity the **Storage Blob Data Contributor** role on the account. Grant the same data-plane role to the Azure CLI user for SDK development. Alternatively, Compose can use `Profiles__AccountName` and `Profiles__Key` in `.env.local`; never commit the key. Blob storage incurs Azure charges.

Users can edit their profile from the header. Usernames are globally unique, case-insensitively, and must contain 3–32 ASCII letters, numbers, underscores or hyphens. Existing users keep their current display name until they choose a username; those legacy names are not reserved. Name changes apply to old messages too. Optional pictures are limited to 512 KB and 4096 pixels per side (16 megapixels), decoded and cropped to a 256×256 PNG, and served only to signed-in users. The original upload is not retained. Replaced pictures are deleted on a best-effort basis, with failures logged.

App Service persists the ASP.NET Core data-protection key ring under its home directory by default, but those keys are **not encrypted at rest**. Configure a protected external key store if required; see [Microsoft's data-protection guidance](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/default-settings). Deployments and restarts clear presence, but stored messages remain.

## Behavior and limitations

- All users who have connected to chat since persistence was enabled are listed as possible direct-message recipients, including offline users. Previous users must connect once to appear in the directory. Presence reflects connected SignalR clients; multiple tabs count as one user. A lost connection is automatically retried, with a manual reconnect option if retries fail.
- Google and Microsoft identities are separate, even when they use the same email address.
- Shared-room and direct messages are stored indefinitely in Cosmos DB. Direct messages are visible only to their two participants. Messages that existed only in memory before persistence was enabled cannot be recovered after restart. Do not use this app for sensitive information.
- Presence and real-time notifications are single-server only; multiple instances require a shared presence and SignalR backplane. There is no moderation or delivery guarantee.
