using Microsoft.Extensions.DependencyInjection;
using OsuRotate.Application;

namespace OsuRotate.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ILobbyStatusReader, IdleLobbyStatusReader>();
        return services;
    }
}
