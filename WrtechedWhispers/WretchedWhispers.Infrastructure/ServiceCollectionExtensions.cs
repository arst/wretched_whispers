using Microsoft.Extensions.DependencyInjection;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Create;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;

namespace WretchedWhispers.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInMemoryInfrastructure(this IServiceCollection services)
    {
        var randomService = new SeededRandomService();
        services.AddSingleton<IRandomService>(_ => randomService);
        Dice.SetRandomGenerator(randomService);
        services.AddSingleton<ICharactersRepository, CharactersInMemoryRepository>();
        services.AddSingleton<ICampaignsRepository, CampaignsInMemoryRepository>();
        services.AddSingleton<IEncountersRepository, EncountersInMemoryRepository>();
        services.AddSingleton<CharacterCreationService>();
        services.AddSingleton<CharacterService>();
        services.AddSingleton<EncounterService>();
        services.AddSingleton<CampaignService>();
        return services;
    }
}