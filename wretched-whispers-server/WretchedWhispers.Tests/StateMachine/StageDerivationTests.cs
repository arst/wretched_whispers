using System.Text.Json;
using System.Text.Json.Nodes;
using Moq;
using WretchedWhispers.Core.Adversaries;
using WretchedWhispers.Core.Campaigns;
using WretchedWhispers.Core.Characters;
using WretchedWhispers.Core.Characters.Possessions.Armors;
using WretchedWhispers.Core.Characters.Possessions.Armors.Tiers;
using WretchedWhispers.Core.Characters.Possessions.Scrolls;
using WretchedWhispers.Core.Characters.Status;
using WretchedWhispers.Core.Dices;
using WretchedWhispers.Core.Encounters;
using WretchedWhispers.Engine.Services;
using Xunit;

namespace WretchedWhispers.Tests.StateMachine;

public class StageDerivationTests : TestBase
{
    [Fact]
    public void No_character_returns_CharacterCreation()
    {
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        // No character ID set, no domain objects loaded

        Assert.Equal(SessionStage.CharacterCreation, ctx.DeriveStage());
    }

    [Fact]
    public void Character_exists_campaign_not_started_returns_CampaignSetup()
    {
        var character = TestCharacters.Create(Dice);
        var campaign = CreateTestCampaign();
        campaign.JoinGame(character.Id);
        // Campaign created with player but NOT started

        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        ctx.SetCharacterId(character.Id);
        ctx.SetCampaignId(campaign.Id);
        ctx.Character = character;
        ctx.Campaign = campaign;

        Assert.Equal(SessionStage.CampaignSetup, ctx.DeriveStage());
    }

    [Fact]
    public void Campaign_active_no_encounter_returns_Exploration()
    {
        var character = TestCharacters.Create(Dice);
        var campaign = CreateTestCampaign();
        campaign.JoinGame(character.Id);
        campaign.Start();

        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        ctx.SetCharacterId(character.Id);
        ctx.SetCampaignId(campaign.Id);
        ctx.Character = character;
        ctx.Campaign = campaign;

        Assert.Equal(SessionStage.Exploration, ctx.DeriveStage());
    }

    [Fact]
    public void Active_encounter_returns_Combat()
    {
        var character = TestCharacters.Create(Dice);
        var campaign = CreateTestCampaign();
        campaign.JoinGame(character.Id);
        campaign.Start();

        // Hostile encounters roll no reaction — no dice script needed.
        var encounter = Encounter.Create("Goblin Fight", "Goblins attack", EncounterType.Hostile, Dice);
        var adversary = CreateMinimalAdversary();
        encounter.AddAdversary(adversary);
        encounter.StartEncounter();

        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        ctx.SetCharacterId(character.Id);
        ctx.SetCampaignId(campaign.Id);
        ctx.SetActiveEncounterId(encounter.Id);
        ctx.Character = character;
        ctx.Campaign = campaign;
        ctx.ActiveEncounter = encounter;

        Assert.Equal(SessionStage.Combat, ctx.DeriveStage());
    }

    [Fact]
    public void Encounter_ended_not_resolved_returns_Resolution()
    {
        var character = TestCharacters.Create(Dice);
        var campaign = CreateTestCampaign();
        campaign.JoinGame(character.Id);
        campaign.Start();

        var encounter = Encounter.Create("Goblin Fight", "Goblins attack", EncounterType.Hostile, Dice);
        var adversary = CreateMinimalAdversary();
        encounter.AddAdversary(adversary);
        encounter.StartEncounter();
        // Kill the adversary so we can end encounter
        KillAdversary(adversary);
        encounter.EndEncounter();
        // Encounter ended but NOT resolved

        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        ctx.SetCharacterId(character.Id);
        ctx.SetCampaignId(campaign.Id);
        ctx.SetActiveEncounterId(encounter.Id);
        ctx.Character = character;
        ctx.Campaign = campaign;
        ctx.ActiveEncounter = encounter;

        Assert.Equal(SessionStage.Resolution, ctx.DeriveStage());
    }

    [Fact]
    public void Character_dead_returns_Ended()
    {
        var character = KillCharacter();
        var campaign = CreateTestCampaign();
        campaign.JoinGame(character.Id);
        campaign.Start();

        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        ctx.SetCharacterId(character.Id);
        ctx.SetCampaignId(campaign.Id);
        ctx.Character = character;
        ctx.Campaign = campaign;

        Assert.True(character.IsDead, "Character should be dead after lethal damage");
        Assert.Equal(SessionStage.Ended, ctx.DeriveStage());
    }

    // Pins the stage -> UI status contract the web client relies on.
    [Theory]
    [InlineData(SessionStage.CharacterCreation, "character-creation")]
    [InlineData(SessionStage.CampaignSetup, "in-progress")]
    [InlineData(SessionStage.Exploration, "in-progress")]
    [InlineData(SessionStage.Combat, "in-progress")]
    [InlineData(SessionStage.Resolution, "in-progress")]
    [InlineData(SessionStage.Ended, "ended")]
    public void StatusFor_maps_stage_to_ui_status(SessionStage stage, string expected)
    {
        Assert.Equal(expected, SessionContext.StatusFor(stage));
    }

    [Fact]
    public void Dead_character_maps_to_fallen_status_even_while_campaign_is_active()
    {
        // Regression: nothing calls Campaign.End() on death, so IsActive() stays true. Status must
        // still be "fallen" (recoverable death) because it derives from the stage (which counts death)
        // plus the world/campaign not being terminally over — not from campaign flags alone.
        var character = KillCharacter();
        var campaign = CreateTestCampaign();
        campaign.JoinGame(character.Id);
        campaign.Start();

        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        ctx.SetCharacterId(character.Id);
        ctx.SetCampaignId(campaign.Id);
        ctx.Character = character;
        ctx.Campaign = campaign;

        Assert.True(character.IsDead);
        Assert.True(campaign.IsActive()); // the trap the old campaign-only status logic fell into
        Assert.Equal("fallen", ctx.DeriveStatus());
    }

    [Fact]
    public async Task DeriveStatus_DeadCharacter_WorldAlsoEnded_IsEnded_NotFallen()
    {
        var character = KillCharacter();
        var campaign = CreateTestCampaign();
        campaign.JoinGame(character.Id);
        campaign.Start();

        // Campaign.AdvanceTime is internal to Core, so the only public way to drive 7 real dawns from
        // this assembly is through CampaignService — same mocked-repository idiom as CampaignServiceTests.
        var campaignsRepository = new Mock<ICampaignsRepository>();
        var charactersRepository = new Mock<ICharactersRepository>();
        campaignsRepository.Setup(r => r.Get(campaign.Id)).ReturnsAsync(campaign);
        charactersRepository.Setup(r => r.Get(character.Id, It.IsAny<CancellationToken>())).ReturnsAsync(character);
        var campaignService = new CampaignService(campaignsRepository.Object, charactersRepository.Object, Dice);

        // 7 dawns: each is a dawn roll of 1 (triggers a misery), a unique two-digit misery code, and the
        // still-joined dead character's daily power reset (StartNewDay rolls its own die) — all drawn from
        // the same dice queue, in call order.
        var rolls = new List<int>();
        for (var day = 0; day < 7; day++)
        {
            rolls.Add(0); // dawn roll -> 1 (triggers)
            rolls.Add(day); // misery die 1 -> day+1
            rolls.Add(0); // misery die 2 -> 1 (codes "11","21",...,"71": all unique)
            rolls.Add(0); // StartNewDay's powers-reset roll (value irrelevant here)
        }
        SetupDiceRolls(rolls.ToArray());

        for (var day = 0; day < 7; day++)
            await campaignService.AdvanceTime(campaign.Id, 24);

        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        ctx.SetCharacterId(character.Id);
        ctx.SetCampaignId(campaign.Id);
        ctx.Character = character;
        ctx.Campaign = campaign;

        Assert.True(campaign.WorldEnded);
        Assert.Equal("ended", ctx.DeriveStatus());
    }

    [Fact]
    public void DeriveStatus_AbandonedCampaign_IsEnded_NotFallen()
    {
        var character = KillCharacter();
        var campaign = CreateTestCampaign();
        campaign.JoinGame(character.Id);
        campaign.Start();
        campaign.End();

        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        ctx.SetCharacterId(character.Id);
        ctx.SetCampaignId(campaign.Id);
        ctx.Character = character;
        ctx.Campaign = campaign;

        Assert.Equal("ended", ctx.DeriveStatus());
    }

    [Fact]
    public void DeriveStatus_BuriedCharacter_ActiveCampaign_IsCharacterCreation()
    {
        var character = KillCharacter();
        var campaign = CreateTestCampaign();
        campaign.JoinGame(character.Id);
        campaign.Start();
        campaign.BuryCharacter(character.Id, character.Name);

        // Post-burial the loader finds no player, so the context has no character.
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        ctx.SetCampaignId(campaign.Id);
        ctx.Campaign = campaign;

        Assert.Equal(SessionStage.CharacterCreation, ctx.DeriveStage());
        Assert.Equal("character-creation", ctx.DeriveStatus());
    }

    [Fact]
    public void FormatSnapshot_contains_character_name_and_hp_without_guids()
    {
        var character = TestCharacters.Create(Dice, maxHp: 10);
        var campaign = CreateTestCampaign();
        campaign.JoinGame(character.Id);
        campaign.Start();

        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        ctx.SetCharacterId(character.Id);
        ctx.SetCampaignId(campaign.Id);
        ctx.Character = character;
        ctx.Campaign = campaign;

        var snapshot = ctx.FormatSnapshot();

        Assert.Contains("TestHero", snapshot);
        Assert.Contains("HP:", snapshot);
        Assert.Contains("10/10", snapshot);
        Assert.Contains("Doom Campaign", snapshot);
        // Must NOT contain any GUIDs
        Assert.DoesNotMatch(@"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", snapshot);
    }

    [Fact]
    public void FormatSnapshot_SaysWhatEachScrollDoes()
    {
        var scroll = new Scroll(Guid.NewGuid(), ScrollSchool.Sacred, "Bestial Speech");
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        ctx.Character = TestCharacters.Create(Dice, scrolls: scroll);

        var snapshot = ctx.FormatSnapshot();

        var power = ScrollPowers.Find("Bestial Speech");
        Assert.NotNull(power);
        Assert.Contains($"- Scroll: Bestial Speech (Sacred): {power.Effect}", snapshot);
    }

    [Fact]
    public void FormatSnapshot_ScrollWithoutAPower_ShowsOnlyItsName()
    {
        var scroll = new Scroll(Guid.NewGuid(), ScrollSchool.Unclean, "random");
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        ctx.Character = TestCharacters.Create(Dice, scrolls: scroll);

        Assert.Contains("- Scroll: random (Unclean)\n", ctx.FormatSnapshot().ReplaceLineEndings("\n"));
    }

    [Fact]
    public void FormatSnapshot_ShowsAPendingBlessing()
    {
        SetupDiceRolls(0, 14, 3); // d4 daily powers; d20 = 15 to cast; d6 = 4
        var scroll = new Scroll(Guid.NewGuid(), ScrollSchool.Sacred, "Grace for a Sinner");
        var hero = TestCharacters.Create(Dice, scrolls: scroll);
        hero.Cast(scroll.Id, Dice);
        var ctx = new SessionContext { SessionId = Guid.NewGuid(), Character = hero };

        Assert.Contains("Blessed: +4 on the next test", ctx.FormatSnapshot());
    }

    [Fact]
    public void FormatSnapshot_ShowsRolledReactionDisposition()
    {
        SetupDiceRolls(1, 1); // 2d6 = 4 -> Angered -> Hostile
        var encounter = Encounter.Create("Strangers", "Figures in the fog", EncounterType.Unknown, Dice);
        encounter.AddAdversary(CreateMinimalAdversary());
        encounter.StartEncounter();

        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        ctx.ActiveEncounter = encounter;

        var snapshot = ctx.FormatSnapshot();

        // Load-bearing tokens only — exact prose is the formatter's business.
        Assert.Contains("Hostile", snapshot);
        Assert.Contains("reaction roll 4", snapshot);
        Assert.Contains("Angered", snapshot);
    }

    [Fact]
    public void FormatSnapshot_ShowsPreDeclaredDispositionWithoutReaction()
    {
        var encounter = Encounter.Create("Guide", "A hired guide", EncounterType.Friendly, Dice);

        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        ctx.ActiveEncounter = encounter;

        var snapshot = ctx.FormatSnapshot();

        Assert.Contains("Friendly", snapshot);
        Assert.DoesNotContain("reaction roll", snapshot);
    }

    [Fact]
    public void FormatSnapshot_ListsFallenWretches()
    {
        var characterId = Guid.NewGuid();
        var campaign = CreateTestCampaign();
        campaign.JoinGame(characterId);
        campaign.Start();
        campaign.BuryCharacter(characterId, "Grimnir");

        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        ctx.SetCampaignId(campaign.Id);
        ctx.Campaign = campaign;

        var snapshot = ctx.FormatSnapshot();

        Assert.Contains("Fallen wretches", snapshot);
        Assert.Contains("Grimnir", snapshot);
    }

    [Fact]
    public void FormatSnapshot_ListsEachAdversaryByName_WithHpArmorAndStatus()
    {
        var encounter = Encounter.Create("Chapel Duel", "Two foes block the road.", EncounterType.Hostile, Dice);
        var priest = new Adversary("Plague Priest", new HitPoints(6, 6), new Armor(ArmorTier.Light), morale: 7,
            new AttackProfile("Brass crucible", DiceExpr.D4));
        var hound = new Adversary("Grave Hound", new HitPoints(4, 4), new Armor(ArmorTier.None), morale: 7,
            new AttackProfile("Bite", DiceExpr.D6));
        var acolyte = new Adversary("Acolyte", new HitPoints(3, 3), new Armor(ArmorTier.None), morale: 7,
            new AttackProfile("Knife", DiceExpr.D4));
        encounter.AddAdversary(priest);
        encounter.AddAdversary(hound);
        encounter.AddAdversary(acolyte);
        encounter.StartEncounter();
        priest.ReceiveDamage(2);
        KillAdversary(hound);
        acolyte.Retreat();

        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        ctx.ActiveEncounter = encounter;

        var snapshot = ctx.FormatSnapshot();

        Assert.Contains("Plague Priest — HP 4/6, armor: Light Armor, alive", snapshot);
        Assert.Contains("Grave Hound — HP 0/4, armor: None, dead", snapshot);
        Assert.Contains("Acolyte — HP 3/3, armor: None, fled", snapshot);
        Assert.DoesNotContain("Living Adversaries", snapshot);
    }

    [Fact]
    public void FormatSnapshot_ShowsInjuriesAndDizziness()
    {
        var character = WithStatus(
            TestCharacters.Create(Dice),
            InjuryKind.LostEye | InjuryKind.BrokenHand,
            dizzyFromMagic: true);
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        ctx.SetCharacterId(character.Id);
        ctx.Character = character;

        var snapshot = ctx.FormatSnapshot();

        Assert.Contains("  Injuries: lost eye, broken hand", snapshot);
        Assert.Contains("DIZZY FROM MAGIC", snapshot);
    }

    [Fact]
    public void FormatSnapshot_UninjuredCharacter_RendersAsBefore()
    {
        // The injury and dizziness lines are omitted when empty, so an unharmed character's snapshot is
        // byte-identical to the format the eval response cache was recorded against.
        var character = TestCharacters.Create(Dice, maxHp: 10);
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        ctx.SetCharacterId(character.Id);
        ctx.Character = character;

        var snapshot = ctx.FormatSnapshot();

        Assert.Equal(
            """
            Character: TestHero
              HP: 10/10
              Strength: 0, Agility: 0
              Presence: 0, Toughness: 0
              Weapon: Sword (d6)
              Armor: None
              Shield: none
              Silver: 10
              Food: 3 days
              Omens: 0
              Inventory (16/16 slots free):

            """.ReplaceLineEndings(Environment.NewLine),
            snapshot);
    }

    // ---- The journal and the map are capped to their most recent part ----

    private static SessionContext ContextFor(Campaign campaign)
    {
        var ctx = new SessionContext { SessionId = Guid.NewGuid() };
        ctx.SetCampaignId(campaign.Id);
        ctx.Campaign = campaign;
        return ctx;
    }

    [Fact]
    public void FormatSnapshot_ShortJournalAndMap_AreShownInFull_WithNoOmissionNote()
    {
        var campaign = CreateTestCampaign();
        campaign.RecordJournalEntry(JournalCategory.Npc, "Grimlod the flagellant owes the character a lantern");
        campaign.RecordJournalEntry(JournalCategory.Promise, "Swore to bury the miller's son");
        campaign.RecordPointOfInterest(PoiType.Town, "Galgenbeck", 50, 50);
        campaign.RecordPointOfInterest(PoiType.Ruin, "The Sunken Chapel", 40, 60, connectedTo: "Galgenbeck");

        var snapshot = ContextFor(campaign).FormatSnapshot();

        Assert.Contains(
            """
              Journal:
                [Day 1, Npc] Grimlod the flagellant owes the character a lantern
                [Day 1, Promise] Swore to bury the miller's son
              Map (0-100 grid, y=0 is north):
                - Galgenbeck (Town) at (50,50)
                - The Sunken Chapel (Ruin) at (40,60), path to Galgenbeck

            """.ReplaceLineEndings(Environment.NewLine),
            snapshot);
        Assert.DoesNotContain("not shown", snapshot);
    }

    [Fact]
    public void FormatSnapshot_LongJournal_ShowsTheMostRecentEntriesWithinItsBudget()
    {
        var campaign = CreateTestCampaign();
        for (var i = 1; i <= 500; i++)
            campaign.RecordJournalEntry(JournalCategory.Event, $"Entry number {i:000}: something durable happened on the road");

        var snapshot = ContextFor(campaign).FormatSnapshot();

        var journalLines = snapshot.Split(Environment.NewLine).Where(l => l.Contains("Entry number")).ToList();
        Assert.InRange(journalLines.Count, 50, 499);
        Assert.True(journalLines.Sum(TokenEstimate.For) <= SessionContext.MaxJournalTokens);
        // The newest are kept, in order; the oldest are the ones left out, and the model is told how many.
        Assert.Contains("Entry number 500", journalLines[^1]);
        Assert.DoesNotContain(journalLines, l => l.Contains("Entry number 001"));
        Assert.Contains($"    ({500 - journalLines.Count} older entries not shown)", snapshot);
    }

    [Fact]
    public void FormatSnapshot_LargeMap_ShowsTheMostRecentPlaces_AndAlwaysWhereThePartyIs()
    {
        var campaign = CreateTestCampaign();
        for (var i = 1; i <= SessionContext.MaxMapPlaces + 5; i++)
            campaign.RecordPointOfInterest(PoiType.Landmark, $"Place {i:00}", i, i);
        campaign.SetPartyLocation("Place 01"); // charted long ago, outside the most recent 40

        var snapshot = ContextFor(campaign).FormatSnapshot();

        Assert.Contains("- Place 01 (Landmark)", snapshot);
        Assert.DoesNotContain("- Place 02 (Landmark)", snapshot);
        Assert.DoesNotContain("- Place 05 (Landmark)", snapshot);
        Assert.Contains("- Place 06 (Landmark)", snapshot);
        Assert.Contains($"- Place {SessionContext.MaxMapPlaces + 5} (Landmark)", snapshot);
        Assert.Contains("    (4 places charted earlier not shown)", snapshot);
        Assert.Contains("  Party location: Place 01", snapshot);
    }

    /// <summary>Rehydrates a character with the given injuries and dizziness through the aggregate's own
    /// JSON shape — the same path the repository uses to load one.</summary>
    private static Character WithStatus(Character character, InjuryKind injuries, bool dizzyFromMagic)
    {
        var options = Infrastructure.Persistence.Serialization.AggregateJsonOptions.Create();
        var node = JsonSerializer.SerializeToNode(character, options)!.AsObject();
        Assert.True(node.ContainsKey("injuries") && node.ContainsKey("isDizzyFromMagic"));
        node["injuries"] = JsonSerializer.SerializeToNode(new InjurySet(injuries), options);
        node["isDizzyFromMagic"] = dizzyFromMagic;
        return node.Deserialize<Character>(options)!;
    }

    private static Campaign CreateTestCampaign()
    {
        return Campaign.Create(Difficulty.Grim, "Doom Campaign", "The world crumbles");
    }

    private static Adversary CreateMinimalAdversary()
    {
        return new Adversary(
            "Goblin",
            new HitPoints(5, 5),
            new Armor(ArmorTier.None),
            morale: 7,
            new AttackProfile("Claw", DiceExpr.D6));
    }

    private static void KillAdversary(Adversary adversary)
    {
        adversary.ReceiveDamage(1000);
    }

    /// <summary>Creates a 1-HP character and kills it with one lethal defend. Every roll is scripted
    /// to 2: d20 defence fails vs DR 12, d6 damage zeroes the single HP, broken-table d4 = 2 -> dead.</summary>
    private Character KillCharacter()
    {
        MockRandomService.Setup(x => x.GenerateRandomRoll(It.IsAny<int>())).Returns(1);
        var character = TestCharacters.Create(Dice, maxHp: 1);
        character.Defend(DiceExpr.D6, Dice);
        return character;
    }
}
