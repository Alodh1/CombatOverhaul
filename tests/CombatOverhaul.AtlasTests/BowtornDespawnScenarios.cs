using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace CombatOverhaul.AtlasTests;

[Trait("Category", "E2E")]
public sealed class BowtornDespawnScenarios : AtlasScenarioBase
{
    private static readonly (string Variant, float MaxRange)[] ExpectedRanges =
    [
        ("surface", 25),
        ("deep", 30),
        ("tainted", 37),
        ("corrupt", 45),
        ("nightmare", 53),
        ("gearfoot", 90),
    ];

    [AtlasScenario(FreshWorld = true, TimeoutMs = 120000)]
    public async Task Bowtorn_CombatAndDespawnRanges_Should_Stay_InSync()
    {
        await World.Ticks(5);

        foreach ((string variant, float maxRange) in ExpectedRanges)
        {
            EntityProperties type = World.Api.World.GetEntityType(new AssetLocation($"game:bowtorn-{variant}"))
                ?? throw new InvalidOperationException($"game:bowtorn-{variant} was not loaded.");
            JsonObject[] behaviors = type.Server.BehaviorsAsJsonObj;
            JsonObject taskAi = behaviors.Single(behavior => behavior["code"].AsString() == "taskai");
            JsonObject turret = taskAi["aitasks"].AsArray()!
                .Single(task => task["code"].AsString() == "CombatOverhaul:TurretMode");
            JsonObject[] despawn = behaviors
                .Where(behavior => behavior["code"].AsString() == "despawn")
                .OrderBy(behavior => behavior["minSeconds"].AsFloat())
                .ToArray();

            Assert.Equal(maxRange, turret["FiringRangeMax"].AsFloat(), precision: 3);
            Assert.Equal(maxRange + 5, turret["SeekingRange"].AsFloat(), precision: 3);
            Assert.Equal(maxRange * 2, despawn[0]["minPlayerDistance"].AsFloat(), precision: 3);
            Assert.Equal(maxRange * 1.2f, despawn[1]["minPlayerDistance"].AsFloat(), precision: 3);
            Assert.True(
                despawn[0]["minPlayerDistance"].AsFloat() > turret["SeekingRange"].AsFloat(),
                $"{variant} Bowtorn can acquire a player outside its six-second despawn radius.");
        }
    }

    [AtlasScenario(FreshWorld = true, TimeoutMs = 120000)]
    public async Task Bowtorn_Should_Not_Expire_While_Player_Is_Inside_Combat_Range()
    {
        await World.Ticks(5);

        var player = await World.JoinPlayer("BowtornGuard");
        BlockPos spawnPosition = player.Position.Offset(3, 0, 0);
        Entity bowtorn = World.SpawnEntity("game:bowtorn-nightmare", spawnPosition);
        long bowtornId = bowtorn.EntityId;
        EntityDespawnData? despawn = null;
        float seekingRange = GetTurretConfig(bowtorn)["SeekingRange"].AsFloat();
        long distanceUpdaterId = World.Api.Event.RegisterGameTickListener(
            _ => bowtorn.NearestPlayerDistance = seekingRange - 1,
            1);

        World.Api.Event.OnEntityDespawn += CaptureBowtornDespawn;
        try
        {
            bowtorn.NearestPlayerDistance = seekingRange - 1;
            await World.Ticks(240);

            Entity? loadedBowtorn = World.Api.World.GetEntityById(bowtornId);
            Assert.True(
                loadedBowtorn?.Alive == true,
                $"Bowtorn {bowtornId} expired while the player was inside its {seekingRange:F1}-block " +
                $"combat range. Reason: {despawn?.Reason}.");
        }
        finally
        {
            World.Api.Event.OnEntityDespawn -= CaptureBowtornDespawn;
            World.Api.Event.UnregisterGameTickListener(distanceUpdaterId);
        }

        void CaptureBowtornDespawn(Entity entity, EntityDespawnData data)
        {
            if (entity.EntityId == bowtornId)
            {
                despawn = data;
            }
        }
    }

    private static JsonObject GetTurretConfig(Entity entity)
    {
        JsonObject taskAi = entity.Properties.Server.BehaviorsAsJsonObj
            .Single(behavior => behavior["code"].AsString() == "taskai");
        return taskAi["aitasks"].AsArray()!
            .Single(task => task["code"].AsString() == "CombatOverhaul:TurretMode");
    }
}
