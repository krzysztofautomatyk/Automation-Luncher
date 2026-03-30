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
        services.AddSingleton<ITiaPortalGateway, TiaPortalGateway>();
        services.AddSingleton<IOperationLogger>(_ => new SerilogOperationLogger(logger));
        services.AddSingleton<ArchiveProjectUseCase>();
        return services;
    }
}
