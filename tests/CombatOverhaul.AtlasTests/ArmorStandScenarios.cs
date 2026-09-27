using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Xunit;

namespace CombatOverhaul.AtlasTests;

[Trait("Category", "E2E")]
public sealed class ArmorStandScenarios : AtlasScenarioBase
{
    [AtlasScenario(TimeoutMs = 120000)]
    public async Task ArmorStand_Should_Use_OnlyCombatOverhaulEquipmentRenderer_AndKeepSelectionBoxes()
    {
        await World.Ticks(5);

        foreach (string entityCode in new[] { "game:armorstand", "game:armorstand-aged" })
        {
            EntityProperties type = World.Api.World.GetEntityType(new AssetLocation(entityCode))
                ?? throw new InvalidOperationException($"{entityCode} was not loaded.");

            AssertArmorStandBehaviors(entityCode, "client", type.Client.BehaviorsAsJsonObj);
            AssertArmorStandBehaviors(entityCode, "server", type.Server.BehaviorsAsJsonObj);
        }
    }

    private static void AssertArmorStandBehaviors(string entityCode, string side, JsonObject[] behaviors)
    {
        string[] codes = behaviors
            .Select(behavior => behavior["code"].AsString() ?? string.Empty)
            .ToArray();

        Assert.True(
            codes.Length >= 4,
            $"{entityCode} {side} behavior list was unexpectedly short: {string.Join(", ", codes)}");
        Assert.Equal("selectionboxes", codes[2]);
        Assert.Equal("CombatOverhaul:ArmorStandInventory", codes[3]);
        Assert.Single(codes, code => code == "selectionboxes");
        Assert.Single(codes, code => code == "CombatOverhaul:ArmorStandInventory");
        Assert.DoesNotContain("rideableaccessories", codes);
    }
}
