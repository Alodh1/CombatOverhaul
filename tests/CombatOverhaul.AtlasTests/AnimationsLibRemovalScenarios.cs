using System.Reflection;
using Atlas.XUnit;
using Xunit;

namespace CombatOverhaul.AtlasTests;

[Trait("Category", "E2E")]
public sealed class AnimationsLibRemovalScenarios : AtlasScenarioBase
{
    [AtlasScenario(TimeoutMs = 120000)]
    public async Task CombatOverhaul_Should_Load_Without_AnimationsLib()
    {
        await World.Ticks(1);

        Assert.True(World.Api.ModLoader.IsModEnabled("combatoverhaulfork"));
        Assert.False(World.Api.ModLoader.IsModEnabled("animationslib"));

        Assembly combatOverhaulAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetType("CombatOverhaul.CombatOverhaulSystem", throwOnError: false) != null)
            ?? throw new InvalidOperationException("CombatOverhaul assembly was not loaded by Atlas.");

        Assert.DoesNotContain(
            combatOverhaulAssembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "animationslib", StringComparison.OrdinalIgnoreCase));
    }
}
