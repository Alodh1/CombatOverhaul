using System.Text;
using Atlas.XUnit;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Xunit;

namespace CombatOverhaul.AtlasTests;

[Trait("Category", "E2E")]
public sealed class ShieldScenarios : AtlasScenarioBase
{
    [AtlasScenario(TimeoutMs = 120000)]
    public async Task MetalRoundShield_Should_Keep_VanillaName_And_DamageAbsorptionTooltip()
    {
        await World.Ticks(5);

        ItemStack shield = CreateMetalRoundShield("iron");
        Assert.Equal("game:shield-metal", shield.Collectible.Code.ToString());
        Assert.Equal("CombatOverhaul.Implementations.VanillaShield", shield.Collectible.GetType().FullName);

        string name = shield.Collectible.GetHeldItemName(shield);
        string expectedName = Lang.GetMatchingIfExists("item-shield-metal-iron-none") ?? "Iron round shield";
        Assert.Equal(expectedName, name);
        Assert.DoesNotContain("shield-withmaterial", name, StringComparison.OrdinalIgnoreCase);

        string tooltip = GetTooltip(shield);
        Assert.Contains("Block chance active use: 90%", tooltip);
        Assert.True(
            tooltip.Contains("5 hp damage absorbed when blocked", StringComparison.Ordinal),
            $"Expected metal round shield tooltip to report 5 hp damage absorption. Tooltip: {tooltip}{DescribeShieldAttributes(shield)}");
        Assert.False(
            tooltip.Contains("0 hp damage absorbed when blocked", StringComparison.Ordinal),
            $"Metal round shield tooltip still reports zero damage absorption. Tooltip: {tooltip}{DescribeShieldAttributes(shield)}");
    }

    private ItemStack CreateMetalRoundShield(string metal)
    {
        Item item = World.Api.World.GetItem(new AssetLocation("game:shield-metal"))
            ?? throw new InvalidOperationException("game:shield-metal was not loaded.");

        ItemStack stack = new(item);
        stack.Attributes.SetString("metal", metal);
        stack.Attributes.SetString("deco", "none");
        return stack;
    }

    private string GetTooltip(ItemStack stack)
    {
        StringBuilder tooltip = new();
        stack.Collectible.GetHeldItemInfo(new DummySlot(stack), tooltip, World.Api.World, withDebugInfo: false);
        return tooltip.ToString();
    }

    private static string DescribeShieldAttributes(ItemStack stack)
    {
        return $@"

ItemAttributes shield: {DescribeJson(stack.ItemAttributes?["shield"])}
Collectible Attributes shield: {DescribeJson(stack.Collectible.Attributes?["shield"])}
";
    }

    private static string DescribeJson(JsonObject? value)
    {
        return value?.Token?.ToString(Formatting.None) ?? "<missing>";
    }
}
