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
using WretchedWhispers.Infrastructure.Persistence.Serialization;
using WretchedWhispers.Semantic;

namespace WretchedWhispers.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers SQLite-backed persistence, domain services, and the dice random generator.
    /// All services use Transient lifetime for compatibility with SemanticKernel's plugin
    /// resolution (ImportPluginFromType resolves from root provider). When a web API host
    /// is added in Phase 3, consider switching to Scoped lifetime with proper scope management.
    /// </summary>
    public static IServiceCollection AddSqliteInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<WretchedWhispersDbContext>(
            options => options.UseSqlite(connectionString),
            ServiceLifetime.Transient);

        var randomService = new SeededRandomService();
        services.AddSingleton<IRandomService>(_ => randomService);
        Dice.SetRandomGenerator(randomService);

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
}
