using Microsoft.Extensions.DependencyInjection;
using SmartPlanif.Application.Abstractions;
using SmartPlanif.Domain.Entities;
using SmartPlanif.Infrastructure.InMemory;
using SmartPlanif.Infrastructure.MySql;
using SmartPlanif.Infrastructure.Settings;

namespace SmartPlanif.Infrastructure;

/// <summary>Enregistrement des adaptateurs d'infrastructure.</summary>
public static class InfrastructureServiceRegistration
{
    /// <summary>Mode démonstration : données générées en mémoire, réglages dans un fichier JSON.</summary>
    public static IServiceCollection AddInMemoryInfrastructure(this IServiceCollection services, string settingsFilePath)
    {
        services.AddSingleton<DemoDataStore>();
        services.AddSingleton<ITripRepository>(sp => sp.GetRequiredService<DemoDataStore>());
        services.AddSingleton<IResourceRepository>(sp => sp.GetRequiredService<DemoDataStore>());
        services.AddSingleton<IAssignmentRepository>(sp => sp.GetRequiredService<DemoDataStore>());
        services.AddSingleton<IReferenceDataRepository>(sp => sp.GetRequiredService<DemoDataStore>());
        services.AddSingleton<IAllocationSettingsStore>(
            new JsonAllocationSettingsStore(settingsFilePath, DemoDataStore.CreateDemoPolicy));
        return services;
    }

    /// <summary>Mode production : lecture/écriture dans la base MySQL Transport_Ai, réglages dans un fichier JSON.</summary>
    public static IServiceCollection AddMySqlInfrastructure(
        this IServiceCollection services, string connectionString, string settingsFilePath,
        Action<QualificationOptions> configureQualifications)
    {
        services.Configure(configureQualifications);
        services.AddSingleton<IDbConnectionFactory>(new MySqlConnectionFactory(connectionString));
        services.AddTransient<ITripRepository, MySqlTripRepository>();
        services.AddTransient<IResourceRepository, MySqlResourceRepository>();
        services.AddTransient<IAssignmentRepository, MySqlAssignmentRepository>();
        services.AddTransient<IReferenceDataRepository, MySqlReferenceDataRepository>();
        services.AddSingleton<IAllocationSettingsStore>(new JsonAllocationSettingsStore(settingsFilePath));
        return services;
    }
}
