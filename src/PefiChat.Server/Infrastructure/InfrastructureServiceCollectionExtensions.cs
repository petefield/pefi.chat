using Azure.Identity;
using Azure.Storage;
using Azure.Storage.Blobs;
using Microsoft.Azure.Cosmos;
using PefiChat.Chat;
using PefiChat.Infrastructure.Chat;
using PefiChat.Infrastructure.Profiles;
using PefiChat.Profiles;
using PefiChat.Push;
using PefiChat.Infrastructure.Push;

namespace PefiChat.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddChatInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton(services =>
        {
            var configuration = services.GetRequiredService<IConfiguration>();
            var endpoint = configuration["Profiles:BlobEndpoint"];
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new InvalidOperationException("Profiles:BlobEndpoint must be configured.");
            var key = configuration["Profiles:Key"];
            var client = string.IsNullOrWhiteSpace(key)
                ? new BlobServiceClient(new Uri(endpoint), new DefaultAzureCredential())
                : new BlobServiceClient(new Uri(endpoint), new StorageSharedKeyCredential(
                    configuration["Profiles:AccountName"] ?? throw new InvalidOperationException("Profiles:AccountName must be configured when using a key."), key));
            return client.GetBlobContainerClient("profiles");
        });
        services.AddSingleton<IProfilePictureStore, BlobProfilePictureStore>();

        services.AddSingleton(services =>
        {
            var configuration = services.GetRequiredService<IConfiguration>();
            var endpoint = configuration["Cosmos:Endpoint"];
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new InvalidOperationException("Cosmos:Endpoint must be configured.");
            var key = configuration["Cosmos:Key"];
            return string.IsNullOrWhiteSpace(key)
                ? new CosmosClient(endpoint, new DefaultAzureCredential(), new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway })
                : new CosmosClient(endpoint, key, new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway });
        });
        services.AddSingleton<IChatRepository, CosmosChatRepository>();
        services.AddSingleton<IPushService, CosmosPushService>();
        return services;
    }
}
