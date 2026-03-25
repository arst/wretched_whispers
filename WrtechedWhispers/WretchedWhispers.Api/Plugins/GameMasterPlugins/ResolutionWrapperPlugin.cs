using System.ComponentModel;
using Microsoft.SemanticKernel;
using WretchedWhispers.Api.Services;

namespace WretchedWhispers.Api.Plugins.GameMasterPlugins;

/// <summary>
/// NEW plugin (no inner equivalent) that signals resolution stage completion.
/// Clears the active encounter from SessionContext so the game returns to exploration.
/// </summary>
[Description("Complete the post-combat resolution phase and return to exploration.")]
public sealed class ResolutionWrapperPlugin(SessionContext sessionContext)
{
    [KernelFunction]
    [Description("Complete the resolution of the current encounter and return to exploration")]
    public Task CompleteResolution()
    {
        if (sessionContext.ActiveEncounterId is null)
            throw new InvalidOperationException("No encounter to resolve.");

        sessionContext.ClearActiveEncounter();
        return Task.CompletedTask;
    }
}
