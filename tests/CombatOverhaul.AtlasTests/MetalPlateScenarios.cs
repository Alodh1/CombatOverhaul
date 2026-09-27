using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace CombatOverhaul.AtlasTests;

[Trait("Category", "E2E")]
public sealed class MetalPlateScenarios : AtlasScenarioBase
{
    [AtlasScenario(TimeoutMs = 120000)]
    public async Task FerrousMetalPlates_Should_Not_Be_Quenchable_Armor()
    {
        await World.Ticks(5);

        foreach (string metal in new[] { "iron", "meteoriciron", "steel" })
        {
            Item plate = World.Api.World.GetItem(new AssetLocation($"game:metalplate-{metal}"))
                ?? throw new InvalidOperationException($"game:metalplate-{metal} was not loaded.");

            Assert.DoesNotContain(
                plate.CollectibleBehaviors,
                behavior => behavior.GetType().FullName == "Vintagestory.GameContent.CollectibleBehaviorQuenchable");
            Assert.NotEqual("armor", plate.Attributes?["quenchBuffKind"].AsString());
            Assert.False(plate.Attributes?["forgable"].AsBool() == true);
        }
    }

    [AtlasScenario(TimeoutMs = 120000)]
    public async Task FerrousMetalPlates_Should_Stack_To_Four_In_Forge()
    {
        await World.Ticks(5);

        var smith = await World.JoinPlayer("PlateSmith");
        smith.Entity.Controls.ShiftKey = true;

        string[] metals = ["iron", "meteoriciron", "steel"];
        for (int index = 0; index < metals.Length; index++)
        {
            string itemCode = $"game:metalplate-{metals[index]}";
            BlockPos forgePosition = World.Spawn.Copy().Add(index * 2, 1, 0);
            World.SetBlock("game:forge", forgePosition);
            await World.Ticks(1);
            await smith.GiveItem(itemCode, 4);

            var selection = new BlockSelection
            {
                Position = forgePosition.Copy(),
                Face = BlockFacing.UP,
                HitPosition = new Vec3d(0.5, 0.5, 0.5)
            };

            for (int plateNumber = 1; plateNumber <= 4; plateNumber++)
            {
                Assert.True(
                    World.BlockAt(forgePosition).OnBlockInteractStart(
                        World.Api.World,
                        smith.Player,
                        selection),
                    $"Forge rejected plate {plateNumber} of 4 for {itemCode}.");
            }

            BlockEntity forge = World.BlockEntityAt<BlockEntity>(forgePosition)
                ?? throw new InvalidOperationException($"Forge block entity was not created at {forgePosition}.");
            ItemStack workItemStack = forge.GetType().GetProperty("WorkItemStack")?.GetValue(forge) as ItemStack
                ?? throw new InvalidOperationException($"Forge at {forgePosition} had no work item stack.");
            Assert.Equal(itemCode, workItemStack.Collectible.Code.ToString());
            Assert.Equal(4, workItemStack.StackSize);
        }
    }
}
