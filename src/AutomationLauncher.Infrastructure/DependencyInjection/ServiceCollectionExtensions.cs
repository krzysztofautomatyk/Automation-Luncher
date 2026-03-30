using AutomationLauncher.Application.UseCases;
using AutomationLauncher.Domain.Contracts;
using AutomationLauncher.Domain.Models;
using AutomationLauncher.Infrastructure.FileSystem;
using AutomationLauncher.Infrastructure.Logging;
using AutomationLauncher.Infrastructure.Tia;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace AutomationLauncher.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, ArchiveOptions options, ILogger logger)
    {
        services.AddSingleton(options);
        services.AddSingleton<IPathService, PathService>();
        services.AddSingleton<ITiaPortalRuntimeCatalog, TiaPortalRuntimeCatalog>();
        services.AddSingleton<TiaPortalRuntimeResolver>();
        services.AddSingleton<IOpennessVersionProvider, V15OpennessVersionProvider>();
        services.AddSingleton<IOpennessVersionProvider, V16OpennessVersionProvider>();
        services.AddSingleton<IOpennessVersionProvider, V17OpennessVersionProvider>();
        services.AddSingleton<IOpennessVersionProvider, V18OpennessVersionProvider>();
        services.AddSingleton<IOpennessVersionProvider, V19OpennessVersionProvider>();
        services.AddSingleton<IOpennessVersionProvider, LatestOpennessVersionProvider>();
        services.AddSingleton<ITiaPortalGateway, TiaPortalGateway>();
        services.AddSingleton<IOperationLogger>(_ => new SerilogOperationLogger(logger));
        services.AddSingleton<ArchiveProjectUseCase>();
        return services;
    }
}
