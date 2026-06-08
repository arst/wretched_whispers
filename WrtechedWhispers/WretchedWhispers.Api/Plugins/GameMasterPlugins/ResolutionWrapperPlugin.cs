using System.ComponentModel;
using WretchedWhispers.Api.Services;
using WretchedWhispers.Core.Encounters;

namespace WretchedWhispers.Api.Plugins.GameMasterPlugins;

/// <summary>
/// NEW plugin (no inner equivalent) that signals resolution stage completion.
/// Marks the encounter as resolved in the database and clears the active encounter
/// from SessionContext so the game returns to exploration.
/// </summary>
[Description("Complete the post-combat resolution phase and return to exploration.")]
public sealed class ResolutionWrapperPlugin(
    SessionContext sessionContext,
    IEncountersRepository encountersRepository)
{
    [Description("Complete the resolution of the current encounter and return to exploration")]
    public async Task CompleteResolution()
    {
        if (sessionContext.ActiveEncounterId is null)
            throw new InvalidOperationException("No encounter to resolve.");

        // Persist IsResolved=true so DeriveStage returns Exploration on next turn
        var encounter = await encountersRepository.Get(sessionContext.ActiveEncounterId.Value);
        if (encounter is not null)
        {
            encounter.Resolve();
            await encountersRepository.Save(encounter);
        }

        sessionContext.ClearActiveEncounter();
    }
}
