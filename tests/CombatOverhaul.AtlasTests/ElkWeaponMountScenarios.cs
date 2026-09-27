using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit;

namespace CombatOverhaul.AtlasTests;

[Trait("Category", "E2E")]
public sealed class ElkWeaponMountScenarios : AtlasScenarioBase
{
    [AtlasScenario(TimeoutMs = 120000)]
    public async Task CoWeapons_Should_Keep_ElkMountAttachmentMetadata()
    {
        await World.Ticks(5);

        AssertElkMountAttachment("game:bow-crude", "bowshort", "weaponr-bow-crude", "weaponl-bow-crude");
        AssertElkMountAttachment("game:bow-simple", "bowshort", "weaponr-bow-simple", "weaponl-bow-simple");
        AssertElkMountAttachment("game:bow-long", "bowlong", "weaponr-bow-long", "weaponl-bow-long");
        AssertElkMountAttachment("game:bow-recurve", "bowlong", "weaponr-bow-recurve", "weaponl-bow-recurve");
        AssertElkMountAttachment("game:blade-falx-iron", "weaponfalx", "weaponr-falx", "weaponl-falx");
        AssertElkMountAttachment("game:blade-blackguard-iron", "weapon1m", "weaponr-blackguard", "weaponl-blackguard");
    }

    private void AssertElkMountAttachment(string itemCode, string categoryCode, string rightShape, string leftShape)
    {
        Item item = World.Api.World.GetItem(new AssetLocation(itemCode))
            ?? throw new InvalidOperationException($"{itemCode} was not loaded.");
        ItemStack stack = new(item);
        JsonObject attachment = stack.ItemAttributes?["attachableToEntity"]
            ?? throw new InvalidOperationException($"{itemCode} has no elk attachment metadata.");

        Assert.Equal(categoryCode, attachment["categoryCode"].AsString());
        Assert.Contains(rightShape, attachment["attachedShapeBySlotCode"]["frontrightside"]["base"].AsString());
        Assert.Contains(leftShape, attachment["attachedShapeBySlotCode"]["frontleftside"]["base"].AsString());
    }
}
