using System.Text.Json;
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

namespace WretchedWhispers.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers repositories, domain services, dice, and JSON options as Scoped.
    /// DbContext must be registered separately by the host (web API uses Scoped lifetime).
    /// </summary>
    public static IServiceCollection AddDomainServices(this IServiceCollection services)
    {
        services.AddSingleton<IRandomService, SeededRandomService>();
        services.AddSingleton<Dice>();
        services.AddScoped<IUserContext, UserContext>();

        services.AddScoped<ICharactersRepository, SqliteCharactersRepository>();
        services.AddScoped<ICampaignsRepository, SqliteCampaignsRepository>();
        services.AddScoped<IEncountersRepository, SqliteEncountersRepository>();
        services.AddScoped<IChatHistoryRepository, SqliteChatHistoryRepository>();
        services.AddScoped<ITurnTraceRepository, SqliteTurnTraceRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<TurnQueue>();
        services.AddSingleton<TurnEventStore>();

        services.AddScoped<CharacterCreationService>();
        services.AddScoped<CharacterService>();
        services.AddScoped<EncounterService>();
        services.AddScoped<CampaignService>();

        services.AddSingleton<JsonSerializerOptions>(_ => AggregateJsonOptions.Create());

        return services;
    }
}
