using System.Reflection;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Xunit;

namespace CombatOverhaul.AtlasTests;

[Trait("Category", "E2E")]
public sealed class PlayerDamageResistanceScenarios : AtlasScenarioBase
{
    private const BindingFlags InstancePublic = BindingFlags.Instance | BindingFlags.Public;
    private const BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;

    [AtlasScenario(TimeoutMs = 120000)]
    public async Task PlayerDamageResistanceStats_Should_StackByFullBodyTypeAndZone()
    {
        await World.Ticks(5);

        var player = await World.JoinPlayer("DamageResist");
        EntityPlayer entity = player.Entity;
        object behavior = entity.GetBehavior("PlayerDamageModel")
            ?? throw new InvalidOperationException("Player did not receive CombatOverhaul:PlayerDamageModel.");
        Type behaviorType = behavior.GetType();
        Type damageZoneType = behaviorType.Assembly.GetType("CombatOverhaul.DamageSystems.DamageZone", throwOnError: true)!;
        MethodInfo getDamageMultiplier = RequireMethod(behaviorType, "GetDamageMultiplier", damageZoneType, typeof(EnumDamageType));
        MethodInfo getDamageTierReduction = RequireMethod(behaviorType, "GetDamageTierReduction", damageZoneType, typeof(EnumDamageType));
        object head = Enum.Parse(damageZoneType, "Head");
        object torso = Enum.Parse(damageZoneType, "Torso");

        Assert.Equal(1f, InvokeFloat(getDamageMultiplier, behavior, torso, EnumDamageType.BluntAttack), 4);
        Assert.Equal(0, InvokeInt(getDamageTierReduction, behavior, torso, EnumDamageType.BluntAttack));

        entity.Stats.Set("playerDamageFactor", "atlas", -0.20f, persistent: false);
        entity.Stats.Set("playerSlashingDamageFactor", "atlas", -0.50f, persistent: false);
        entity.Stats.Set("playerDamageTierReduction", "atlas", 1, persistent: false);
        entity.Stats.Set("playerSlashingDamageTierReduction", "atlas", 2, persistent: false);

        DamageSource source = new()
        {
            Type = EnumDamageType.SlashingAttack,
            DamageTier = 5
        };
        RequireMethod(behaviorType, "OnReceiveDamageHandler", InstanceNonPublic, typeof(float), typeof(DamageSource))
            .Invoke(behavior, [0f, source]);
        Assert.Equal(2, source.DamageTier);

        entity.Stats.Set("playerHeadDamageFactor", "atlas", -0.25f, persistent: false);
        entity.Stats.Set("playerHeadDamageTierReduction", "atlas", 3, persistent: false);

        Assert.Equal(0.30f, InvokeFloat(getDamageMultiplier, behavior, head, EnumDamageType.SlashingAttack), 4);
        Assert.Equal(0.60f, InvokeFloat(getDamageMultiplier, behavior, head, EnumDamageType.PiercingAttack), 4);
        Assert.Equal(0.80f, InvokeFloat(getDamageMultiplier, behavior, torso, EnumDamageType.BluntAttack), 4);
        Assert.Equal(6, InvokeInt(getDamageTierReduction, behavior, head, EnumDamageType.SlashingAttack));
        Assert.Equal(4, InvokeInt(getDamageTierReduction, behavior, head, EnumDamageType.PiercingAttack));
        Assert.Equal(1, InvokeInt(getDamageTierReduction, behavior, torso, EnumDamageType.BluntAttack));
    }

    private static MethodInfo RequireMethod(Type type, string name, params Type[] parameterTypes)
    {
        return RequireMethod(type, name, InstancePublic, parameterTypes);
    }

    private static MethodInfo RequireMethod(Type type, string name, BindingFlags flags, params Type[] parameterTypes)
    {
        return type.GetMethod(name, flags, null, parameterTypes, null)
            ?? throw new MissingMethodException(type.FullName, name);
    }

    private static float InvokeFloat(MethodInfo method, object target, params object[] arguments)
    {
        return (float)(method.Invoke(target, arguments)
            ?? throw new InvalidOperationException($"{method.Name} returned null."));
    }

    private static int InvokeInt(MethodInfo method, object target, params object[] arguments)
    {
        return (int)(method.Invoke(target, arguments)
            ?? throw new InvalidOperationException($"{method.Name} returned null."));
    }
}
