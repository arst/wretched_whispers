using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using WretchedWhispers.Infrastructure.Persistence;
using WretchedWhispers.Infrastructure.Persistence.Repositories;
using WretchedWhispers.Core;
using WretchedWhispers.Infrastructure.Persistence.Serialization;
using WretchedWhispers.Semantic;

namespace WretchedWhispers.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers SQLite DbContext (Transient) plus all domain services.
    /// Transient lifetime needed for SemanticKernel's root-provider plugin resolution.
    /// </summary>
    public static IServiceCollection AddSqliteInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<WretchedWhispersDbContext>(
            options => options.UseSqlite(connectionString),
            ServiceLifetime.Transient);

        services.AddSingleton<IRandomService, SeededRandomService>();
        services.AddSingleton<Dice>();
        services.AddSingleton<ITenantContext, TenantContext>();

        // Repositories as Transient (compatible with SK's root-provider plugin resolution)
        services.AddTransient<ICharactersRepository, SqliteCharactersRepository>();
        services.AddTransient<ICampaignsRepository, SqliteCampaignsRepository>();
        services.AddTransient<IEncountersRepository, SqliteEncountersRepository>();
        services.AddTransient<IChatHistoryRepository, SqliteChatHistoryRepository>();

        // Domain services as Transient
        services.AddTransient<CharacterCreationService>();
        services.AddTransient<CharacterService>();
        services.AddTransient<EncounterService>();
        services.AddTransient<CampaignService>();

        // Register JsonSerializerOptions for aggregate serialization
        services.AddSingleton<JsonSerializerOptions>(_ => AggregateJsonOptions.Create());

        return services;
    }

    /// <summary>
    /// Registers repositories, domain services, dice, and JSON options as Scoped.
    /// DbContext must be registered separately by the host (web API uses Scoped lifetime).
    /// </summary>
    public static IServiceCollection AddDomainServices(this IServiceCollection services)
    {
        services.AddSingleton<IRandomService, SeededRandomService>();
        services.AddSingleton<Dice>();
        services.AddScoped<ITenantContext, TenantContext>();

        services.AddScoped<ICharactersRepository, SqliteCharactersRepository>();
        services.AddScoped<ICampaignsRepository, SqliteCampaignsRepository>();
        services.AddScoped<IEncountersRepository, SqliteEncountersRepository>();
        services.AddScoped<IChatHistoryRepository, SqliteChatHistoryRepository>();

        services.AddScoped<CharacterCreationService>();
        services.AddScoped<CharacterService>();
        services.AddScoped<EncounterService>();
        services.AddScoped<CampaignService>();

        services.AddSingleton<JsonSerializerOptions>(_ => AggregateJsonOptions.Create());

        return services;
    }
}
