using System.Collections;
using System.Reflection;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Xunit;

namespace CombatOverhaul.AtlasTests;

[Trait("Category", "E2E")]
public sealed class ShieldBlockPacketSecurityScenarios : AtlasScenarioBase
{
    private const BindingFlags StaticNonPublic = BindingFlags.Static | BindingFlags.NonPublic;
    private const BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags InstancePublic = BindingFlags.Instance | BindingFlags.Public;

    [AtlasScenario(FreshWorld = true, TimeoutMs = 120000)]
    public async Task ShieldBlockPacket_Should_RejectForgedProfile_AndAcceptConfiguredProfile()
    {
        await World.Ticks(5);

        var player = await World.JoinPlayer("BlockGuard");
        object behavior = player.Entity.GetBehavior("PlayerDamageModel")
            ?? throw new InvalidOperationException("Player did not receive CombatOverhaul:PlayerDamageModel.");
        PropertyInfo currentBlock = RequireProperty(behavior.GetType(), "CurrentDamageBlock");

        Item shieldItem = World.Api.World.GetItem(new AssetLocation("game:shield-metal"))
            ?? throw new InvalidOperationException("game:shield-metal was not loaded.");
        ItemStack shield = new(shieldItem);
        shield.Attributes.SetString("metal", "iron");
        shield.Attributes.SetString("deco", "none");
        player.Entity.LeftHandItemSlot.Itemstack = shield;

        Assembly overhaulAssembly = behavior.GetType().Assembly;
        Type packetType = RequireType(overhaulAssembly, "CombatOverhaul.DamageSystems.DamageBlockPacket");
        Type profilesType = RequireType(overhaulAssembly, "CombatOverhaul.DamageSystems.ServerDamageBlockProfiles");
        MethodInfo getAllowedPackets = RequireMethod(
            profilesType,
            "GetAllowedPackets",
            StaticNonPublic,
            typeof(Vintagestory.API.Server.IServerPlayer),
            typeof(bool));
        IEnumerable allowedPackets = (IEnumerable)(getAllowedPackets.Invoke(null, [player.Player, false])
            ?? throw new InvalidOperationException("Server profile resolver returned null."));
        object configuredPacket = allowedPackets.Cast<object>()
            .FirstOrDefault(packet => GetProperty(packet, "Kind").ToString() == "Block")
            ?? throw new InvalidOperationException("Server did not derive a configured block profile for the held shield.");
        MethodInfo tryResolve = RequireMethod(
            profilesType,
            "TryResolve",
            StaticNonPublic,
            typeof(Vintagestory.API.Server.IServerPlayer),
            packetType,
            packetType.MakeByRefType());

        object forgedPacket = Activator.CreateInstance(packetType)
            ?? throw new InvalidOperationException("Could not create forged DamageBlockPacket.");
        SetProperty(forgedPacket, "Zones", 0x0FFF);
        SetProperty(forgedPacket, "Directions", new[] { 180f, 180f, 180f, 180f });
        SetProperty(forgedPacket, "MainHand", false);
        SetProperty(forgedPacket, "Sound", null);
        SetProperty(forgedPacket, "BlockTier", null);
        SetProperty(forgedPacket, "CanBlockProjectiles", true);
        SetProperty(forgedPacket, "StaggerTimeMs", 0);
        SetProperty(forgedPacket, "StaggerTier", 1);
        SetProperty(forgedPacket, "Kind", Enum.Parse(RequireType(overhaulAssembly, "CombatOverhaul.DamageSystems.EnumDamageBlockKind"), "Block"));
        SetProperty(forgedPacket, "Id", 77UL);

        object?[] forgedResolution = [player.Player, forgedPacket, null];
        Assert.False((bool)tryResolve.Invoke(null, forgedResolution)!);
        object?[] configuredResolution = [player.Player, configuredPacket, null];
        bool configuredAccepted = (bool)tryResolve.Invoke(null, configuredResolution)!;
        if (!configuredAccepted)
        {
            IEnumerable regenerated = (IEnumerable)getAllowedPackets.Invoke(null, [player.Player, false])!;
            throw new InvalidOperationException(
                $"Configured profile changed between server resolutions. Requested: {DescribePacket(configuredPacket)}; " +
                $"Regenerated: {string.Join(" | ", regenerated.Cast<object>().Select(DescribePacket))}");
        }

        object overhaulSystem = World.Api.ModLoader.Systems
            .Single(system => system.GetType().FullName == "CombatOverhaul.CombatOverhaulSystem");
        object blockSystem = RequireProperty(overhaulSystem.GetType(), "ServerBlockSystem")
            .GetValue(overhaulSystem)
            ?? throw new InvalidOperationException("Combat Overhaul server block system was not initialized.");
        MethodInfo handlePacket = RequireMethod(blockSystem.GetType(), "HandlePacket", InstanceNonPublic, typeof(Vintagestory.API.Server.IServerPlayer), packetType);

        handlePacket.Invoke(blockSystem, [player.Player, forgedPacket]);
        Assert.Null(currentBlock.GetValue(behavior));

        handlePacket.Invoke(blockSystem, [player.Player, configuredPacket]);
        object accepted = currentBlock.GetValue(behavior)
            ?? throw new InvalidOperationException("Server rejected the shield's own configured block profile.");
        Assert.Equal("Block", GetField(accepted, "Kind").ToString());
        Assert.False((bool)GetField(accepted, "MainHand"));

        Type bodyPartType = RequireType(overhaulAssembly, "CombatOverhaul.DamageSystems.PlayerBodyPart");
        MethodInfo applyBlock = RequireMethod(
            behavior.GetType(),
            "ApplyBlock",
            InstanceNonPublic,
            typeof(DamageSource),
            bodyPartType,
            typeof(float).MakeByRefType(),
            typeof(string).MakeByRefType());

        object?[] authorizedBlockAttempt =
        [
            new DamageSource { Type = EnumDamageType.BluntAttack, DamageTier = 0 },
            Enum.Parse(bodyPartType, "Torso"),
            5f,
            null
        ];
        applyBlock.Invoke(behavior, authorizedBlockAttempt);
        Assert.Equal(0f, (float)authorizedBlockAttempt[2]!);
        Assert.NotNull(currentBlock.GetValue(behavior));

        player.Entity.LeftHandItemSlot.Itemstack = null;
        object?[] staleBlockAttempt =
        [
            new DamageSource { Type = EnumDamageType.BluntAttack, DamageTier = 0 },
            Enum.Parse(bodyPartType, "Torso"),
            5f,
            null
        ];
        applyBlock.Invoke(behavior, staleBlockAttempt);
        Assert.Equal(5f, (float)staleBlockAttempt[2]!);
        Assert.Null(currentBlock.GetValue(behavior));
    }

    private static Type RequireType(Assembly assembly, string fullName)
    {
        return assembly.GetType(fullName, throwOnError: true)!;
    }

    private static MethodInfo RequireMethod(Type type, string name, BindingFlags flags, params Type[] parameterTypes)
    {
        return type.GetMethod(name, flags, null, parameterTypes, null)
            ?? throw new MissingMethodException(type.FullName, name);
    }

    private static PropertyInfo RequireProperty(Type type, string name)
    {
        return type.GetProperty(name, InstancePublic)
            ?? throw new MissingMemberException(type.FullName, name);
    }

    private static object GetProperty(object target, string name)
    {
        return RequireProperty(target.GetType(), name).GetValue(target)
            ?? throw new InvalidOperationException($"{target.GetType().FullName}.{name} returned null.");
    }

    private static object GetField(object target, string name)
    {
        return target.GetType().GetField(name, InstancePublic)?.GetValue(target)
            ?? throw new MissingFieldException(target.GetType().FullName, name);
    }

    private static void SetProperty(object target, string name, object? value)
    {
        RequireProperty(target.GetType(), name).SetValue(target, value);
    }

    private static string DescribePacket(object packet)
    {
        IEnumerable directions = (IEnumerable)GetProperty(packet, "Directions");
        object? tiers = RequireProperty(packet.GetType(), "BlockTier").GetValue(packet);
        string tierText = tiers is IDictionary dictionary
            ? string.Join(",", dictionary.Keys.Cast<object>().Select(key => $"{key}={dictionary[key]}"))
            : "null";
        return $"kind={GetProperty(packet, "Kind")}, zones={GetProperty(packet, "Zones")}, " +
            $"directions={string.Join(",", directions.Cast<object>())}, sound={RequireProperty(packet.GetType(), "Sound").GetValue(packet) ?? "null"}, " +
            $"tiers={tierText}, projectiles={GetProperty(packet, "CanBlockProjectiles")}, " +
            $"stagger={GetProperty(packet, "StaggerTimeMs")}/{GetProperty(packet, "StaggerTier")}";
    }
}
