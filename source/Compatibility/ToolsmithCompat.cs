using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Globalization;
using System.Text;
using CombatOverhaul.DamageSystems;
using CombatOverhaul.Implementations;
using CombatOverhaul.Integration;
using CombatOverhaul.Inputs;
using CombatOverhaul.MeleeSystems;
using CombatOverhaul.Utils;
using CombatOverhaul.WeaponBuffs;
using HarmonyLib;
using OpenTK.Mathematics;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace CombatOverhaul.Compatibility;

internal static class ToolsmithCompat
{
    private const string HarmonyId = "combatoverhaulfork.toolsmith.compat";
    private const string ToolsmithSpeedBonusAttribute = "speedBonus";
    private const string ToolsmithSharpnessCurrentAttribute = "toolSharpnessCurrent";
    private const string ToolsmithSharpnessMaxAttribute = "toolSharpnessMax";
    private const string ToolsmithToolHeadAttribute = "tinkeredToolHead";
    private const string ToolsmithToolHandleAttribute = "tinkeredToolHandle";
    private const string ToolsmithToolDurabilityAttribute = "durability";
    private const string ToolsmithToolHandleCurrentDurabilityAttribute = "tinkeredToolHandleDurability";
    private const string ToolsmithToolHandleMaxDurabilityAttribute = "tinkeredToolHandleMaxDurability";
    private const string ToolsmithToolBindingCurrentDurabilityAttribute = "tinkeredToolBindingDurability";
    private const string ToolsmithToolBindingMaxDurabilityAttribute = "tinkeredToolBindingMaxDurability";
    private const string ToolsmithToolHeadBehaviorName = "CollectibleBehaviorToolHead";
    private const string ToolsmithToolHandleBehaviorName = "CollectibleBehaviorToolHandle";
    private const string ToolsmithTinkeredToolBehaviorName = "CollectibleBehaviorTinkeredTools";
    private const string ToolsmithTinkeringNetworkChannel = "CombatOverhaul:toolsmith-tinkering";
    private const string BleedParticleEffectCode = "combatoverhaul:bleed";
    private const string ToolsmithCompatAssembledAttribute = "combatoverhaulToolsmithAssembledWeapon";
    private const string ToolsmithAttackSpeedBuffCode = "toolsmith-handle-speed";
    private const string ToolsmithAttackSpeedBuffSource = "toolsmith";
    private const string ToolsmithAttackSpeedFallbackAttribute = "combatoverhaulToolsmithAttackSpeedBonus";
    private const string AttackSpeedAttribute = "attackSpeed";
    private const string ModularMultiPartRenderDataAttribute = "modularMultiPartRenderData";
    private const string ModularPartRenderDataAttribute = "modularPartRenderData";
    private const string PartShapeIndexAttribute = "partShapeIndex";
    private const string ToolsmithMeshRefIdAttribute = "toolsmithMeshrefID";
    private const string ActiveTinkeringActionAttribute = "combatoverhaulToolsmithActiveTinkeringAction";
    private const string ActiveTinkeringMaterialAttribute = "combatoverhaulToolsmithActiveTinkeringMaterial";
    private const string ActiveTinkeringStartedMsAttribute = "combatoverhaulToolsmithActiveTinkeringStartedMs";
    private const string ActiveTinkeringLastMsAttribute = "combatoverhaulToolsmithActiveTinkeringLastMs";
    private const string ActiveTinkeringSubmittedAttribute = "combatoverhaulToolsmithActiveTinkeringSubmitted";
    private const string SuppressToolsmithDurabilityReductionAttribute = "combatoverhaulToolsmithSuppressDurabilityReduction";
    private const string ToolsmithDurabilityReducedAttribute = "combatoverhaulToolsmithDurabilityReduced";
    private const string SerratedAttribute = "combatoverhaulToolsmithSerrated";
    private const string NeedlePointAttribute = "combatoverhaulToolsmithNeedlePoint";
    private const string NeedlePointArmorPiercingAppliedAttribute = "combatoverhaulToolsmithNeedlePointArmorPiercingApplied";
    private const string NailMaterialAttribute = "combatoverhaulToolsmithNailMaterial";
    private const string NailCurrentDurabilityAttribute = "combatoverhaulToolsmithNailCurrentDurability";
    private const string NailMaxDurabilityAttribute = "combatoverhaulToolsmithNailMaxDurability";
    private const string AttackSpeedTooltipColor = "#00bb00";
    private const string CritChanceTooltipColor = "#bb0000";
    private const string SerratedTooltipColor = "#bb3030";
    private const string NailTooltipColor = "#d0a030";
    private const string ArmorPiercingTooltipColor = "#c0c0ff";
    private const string LegacyAttackSpeedTooltipName = "Toolsmith handle speed";
    private const string LegacyAttackSpeedTooltipDescription = "Converts Toolsmith handle use speed into melee attack speed.";
    private const float ToolsmithMaxSharpnessCritChance = 0.05f;
    private const float ToolsmithWeaponDurabilityFactor = 0.5f;
    private const float ToolsmithTinkeringDurationSeconds = 2.5f;
    private const float ToolsmithTinkeringCompletionGraceSeconds = 0.1f;
    private const float SerratedDurabilityFactor = 0.85f;
    private const float NeedlePointDurabilityFactor = 0.90f;
    private const float SerratedBleedDurationSeconds = 4f;
    private const float SerratedBleedDamageFraction = 0.40f;
    private const int SerratedBleedTicks = 4;
    private const float NailPierceDamage = 2f;
    private const int NailArmorPiercingBonus = 1;
    private const int BaseNailDurability = 80;
    private const int NeedlePointArmorPiercingBonus = 1;
    private const long ActiveTinkeringInputGapMilliseconds = 400;

    private enum TinkeringActionType
    {
        None,
        Serrate,
        AttachNails,
        NeedlePoint
    }

    private readonly struct TinkeringAction
    {
        public readonly TinkeringActionType Type;
        public readonly string Material;

        public TinkeringAction(TinkeringActionType type, string material = "")
        {
            Type = type;
            Material = material;
        }
    }

    private sealed class BleedState
    {
        public int TicksRemaining;
        public float DamagePerTick;
        public int DamageTier;
        public int ArmorPiercingTier;
        public long SourceEntityId;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public sealed class ToolsmithTinkeringPacket
    {
        public string Action { get; set; } = "";
        public string Material { get; set; } = "";
    }

    private static Harmony? _harmony;
    private static ICoreServerAPI? _serverApi;
    private static IClientNetworkChannel? _clientTinkeringChannel;
    private static IServerNetworkChannel? _serverTinkeringChannel;
    private static long _bleedTickListenerId;
    private static bool _genericPatchesApplied;
    private static bool _toolsmithPatchesApplied;
    private static bool _disabledAssemblyBlockerApplied;
    private static bool _toolsmithWeaponCraftingEnabled;
    private static bool _criticalChanceProviderRegistered;
    [ThreadStatic]
    private static int _toolsmithWorkbenchAssemblyDepth;
    private static readonly FieldInfo? ActionsManagerPlayerField = AccessTools.Field(typeof(ActionsManagerPlayerBehavior), "_player");
    private static readonly Dictionary<long, BleedState> Bleeds = new();
    private static readonly ConcurrentDictionary<CollectibleObject, bool> KeepBaseWeaponVisualCache = new();
    private static readonly Dictionary<string, float> NailDurabilityFactors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["copper"] = 1.4f,
        ["tinbronze"] = 1.7f,
        ["bismuthbronze"] = 1.7f,
        ["blackbronze"] = 1.7f,
        ["cupronickel"] = 1.7f,
        ["iron"] = 1.8f,
        ["meteoriciron"] = 1.9f,
        ["steel"] = 2.2f,
        ["meteoricsteel"] = 2.3f,
        ["uraniumsteel"] = 2.35f,
        ["ferrousuranium"] = 1.7f
    };

    public static void Patch(ICoreAPI api)
    {
        if (_disabledAssemblyBlockerApplied) Unpatch();

        PatchGenericDamageHandling();
        RegisterCriticalChanceProvider(api);
        RegisterServerState(api);

        if (!api.ModLoader.IsModEnabled("toolsmith")) return;

        AddTinkeringIngredientStorageFlags(api);
        RegisterTinkeringNetwork(api);
        RemoveToolsmithGeneratedWeaponGridRecipes(api);

        _toolsmithWeaponCraftingEnabled = true;
        if (_toolsmithPatchesApplied) return;

        Type? tinkeredToolsBehavior = AccessTools.TypeByName("Toolsmith.ToolTinkering.Behaviors.CollectibleBehaviorTinkeredTools");
        Type? tinkeringUtility = AccessTools.TypeByName("Toolsmith.ToolTinkering.TinkeringUtility");
        Type? toolBindingBehavior = AccessTools.TypeByName("Toolsmith.ToolTinkering.Behaviors.CollectibleBehaviorToolBinding");
        Type? modularRenderingBehavior = AccessTools.TypeByName("Toolsmith.Client.Behaviors.ModularPartRenderingFromAttributes");

        if (tinkeredToolsBehavior == null)
        {
            api.Logger.Warning("Combat Overhaul could not patch Toolsmith weapon visuals; supported Toolsmith-assembled weapons may render as multipart tools.");
            return;
        }

        MethodInfo? onCreatedByCrafting = AccessTools.Method(tinkeredToolsBehavior, "OnCreatedByCrafting");
        Type? recipeRegister = AccessTools.TypeByName("Toolsmith.RecipeRegisterModSystem");

        if (onCreatedByCrafting == null)
        {
            api.Logger.Warning("Combat Overhaul could not patch Toolsmith weapon visuals; supported Toolsmith-assembled weapons may render as multipart tools.");
            return;
        }

        _harmony ??= new Harmony(HarmonyId);
        _harmony.Patch(
            onCreatedByCrafting,
            prefix: new HarmonyMethod(typeof(ToolsmithCompat), nameof(BeforeToolsmithCreatedTool)),
            postfix: new HarmonyMethod(typeof(ToolsmithCompat), nameof(AfterToolsmithCreatedTool)));
        PatchCollectibleCreatedByCraftingCleanup();

        MethodInfo? tryCraftToolFromSlots = tinkeringUtility == null ? null : AccessTools.Method(tinkeringUtility, "TryCraftToolFromSlots");
        if (tryCraftToolFromSlots != null)
        {
            _harmony.Patch(
                tryCraftToolFromSlots,
                prefix: new HarmonyMethod(typeof(ToolsmithCompat), nameof(BeforeToolsmithTryCraftToolFromSlots)),
                postfix: new HarmonyMethod(typeof(ToolsmithCompat), nameof(AfterToolsmithTryCraftToolFromSlots)));
        }

        MethodInfo? assetsFinalize = recipeRegister == null
            ? null
            : AccessTools.Method(recipeRegister, nameof(ModSystem.AssetsFinalize), [typeof(ICoreAPI)]);
        if (assetsFinalize != null)
        {
            _harmony.Patch(assetsFinalize, postfix: new HarmonyMethod(typeof(ToolsmithCompat), nameof(AfterToolsmithRecipeRegisterAssetsFinalize)));
        }

        PatchToolsmithBehaviorMethod(tinkeredToolsBehavior, "GetHeldItemInfo", nameof(BeforeToolsmithHeldItemInfo));
        PatchToolsmithBehaviorMethod(tinkeredToolsBehavior, "OnDamageItem", nameof(BeforeToolsmithDamageItem));
        PatchToolsmithBehaviorMethod(tinkeredToolsBehavior, "GetMaxDurability", nameof(BeforeToolsmithStackBehavior));
        PatchToolsmithBehaviorMethod(tinkeredToolsBehavior, "GetMiningSpeed", nameof(BeforeToolsmithStackBehavior));
        if (toolBindingBehavior != null)
        {
            PatchToolsmithBehaviorMethod(toolBindingBehavior, "GetHeldItemInfo", nameof(BeforeToolsmithBindingHeldItemInfo));
        }
        PatchCollectibleObjectMethod(
            nameof(CollectibleObject.OnHeldUseStart),
            [
                typeof(ItemSlot),
                typeof(EntityAgent),
                typeof(BlockSelection),
                typeof(EntitySelection),
                typeof(EnumHandInteract),
                typeof(bool),
                typeof(EnumHandHandling).MakeByRefType()
            ],
            nameof(BeforeHeldUseStart));
        PatchCollectibleObjectMethod(
            nameof(CollectibleObject.OnHeldUseStep),
            [
                typeof(float),
                typeof(ItemSlot),
                typeof(EntityAgent),
                typeof(BlockSelection),
                typeof(EntitySelection)
            ],
            nameof(BeforeHeldUseStep));
        PatchCollectibleObjectMethod(
            nameof(CollectibleObject.OnHeldUseStop),
            [
                typeof(float),
                typeof(ItemSlot),
                typeof(EntityAgent),
                typeof(BlockSelection),
                typeof(EntitySelection),
                typeof(EnumHandInteract)
            ],
            nameof(BeforeHeldUseStop));
        PatchCollectibleObjectMethod(
            nameof(CollectibleObject.OnHeldUseCancel),
            [
                typeof(float),
                typeof(ItemSlot),
                typeof(EntityAgent),
                typeof(BlockSelection),
                typeof(EntitySelection),
                typeof(EnumItemUseCancelReason)
            ],
            nameof(BeforeHeldUseCancel));

        MethodInfo? getHeldItemInfo = AccessTools.Method(
            typeof(CollectibleObject),
            nameof(CollectibleObject.GetHeldItemInfo),
            [
                typeof(ItemSlot),
                typeof(StringBuilder),
                typeof(IWorldAccessor),
                typeof(bool)
            ]);
        if (getHeldItemInfo != null)
        {
            _harmony.Patch(getHeldItemInfo, postfix: new HarmonyMethod(typeof(ToolsmithCompat), nameof(AfterCollectibleHeldItemInfo)));
        }

        MethodInfo? getMaxDurability = AccessTools.Method(typeof(CollectibleObject), nameof(CollectibleObject.GetMaxDurability), [typeof(ItemStack)]);
        if (getMaxDurability != null)
        {
            _harmony.Patch(getMaxDurability, postfix: new HarmonyMethod(typeof(ToolsmithCompat), nameof(AfterGetMaxDurability)));
        }

        MethodInfo? appendTooltip = AccessTools.Method(typeof(WeaponBuffSystem), nameof(WeaponBuffSystem.AppendTooltip));
        if (appendTooltip != null)
        {
            _harmony.Patch(appendTooltip, postfix: new HarmonyMethod(typeof(ToolsmithCompat), nameof(AfterWeaponBuffTooltip)));
        }

        MethodInfo? handleCombatOverhaulActionEvent = AccessTools.Method(
            typeof(ActionsManagerPlayerBehavior),
            "HandleActionEvent",
            [
                typeof(ActionEventData),
                typeof(int),
                typeof(ActionsManagerPlayerBehavior.ActionEventCallbackDelegate)
            ]);
        if (handleCombatOverhaulActionEvent != null)
        {
            _harmony.Patch(handleCombatOverhaulActionEvent, prefix: new HarmonyMethod(typeof(ToolsmithCompat), nameof(BeforeCombatOverhaulActionEvent)));
        }

        MethodInfo? onBeforeRender = modularRenderingBehavior == null ? null : AccessTools.Method(modularRenderingBehavior, "OnBeforeRender");
        if (onBeforeRender != null)
        {
            _harmony.Patch(onBeforeRender, prefix: new HarmonyMethod(typeof(ToolsmithCompat), nameof(BeforeToolsmithMultipartRender)));
        }

        _toolsmithPatchesApplied = true;
    }

    public static void PatchDisabledAssemblyBlocker(ICoreAPI api)
    {
        if (_toolsmithPatchesApplied || _genericPatchesApplied || _criticalChanceProviderRegistered)
        {
            Unpatch();
        }

        if (!api.ModLoader.IsModEnabled("toolsmith") || _disabledAssemblyBlockerApplied) return;

        Type? tinkeringUtility = AccessTools.TypeByName("Toolsmith.ToolTinkering.TinkeringUtility");
        Type? tinkeredToolsBehavior = AccessTools.TypeByName("Toolsmith.ToolTinkering.Behaviors.CollectibleBehaviorTinkeredTools");
        if (tinkeringUtility == null || tinkeredToolsBehavior == null) return;

        _harmony ??= new Harmony(HarmonyId);
        _toolsmithWeaponCraftingEnabled = false;
        DisableToolsmithForCombatOverhaulWeapons(api);
        PatchCollectibleCreatedByCraftingCleanup();

        MethodInfo? tryCraftToolFromSlots = AccessTools.Method(tinkeringUtility, "TryCraftToolFromSlots");
        if (tryCraftToolFromSlots != null)
        {
            _harmony.Patch(tryCraftToolFromSlots, prefix: new HarmonyMethod(typeof(ToolsmithCompat), nameof(BeforeDisabledTryCraftToolFromSlots)));
        }

        MethodInfo? assemblePartBundle = AccessTools.Method(tinkeringUtility, "AssemblePartBundle");
        if (assemblePartBundle != null)
        {
            _harmony.Patch(assemblePartBundle, prefix: new HarmonyMethod(typeof(ToolsmithCompat), nameof(BeforeDisabledAssemblePartBundle)));
        }

        MethodInfo? assembleFullTool = AccessTools.Method(tinkeringUtility, "AssembleFullTool");
        if (assembleFullTool != null)
        {
            _harmony.Patch(assembleFullTool, prefix: new HarmonyMethod(typeof(ToolsmithCompat), nameof(BeforeDisabledAssembleFullTool)));
        }

        MethodInfo? onCreatedByCrafting = AccessTools.Method(tinkeredToolsBehavior, "OnCreatedByCrafting");
        if (onCreatedByCrafting != null)
        {
            _harmony.Patch(
                onCreatedByCrafting,
                prefix: new HarmonyMethod(typeof(ToolsmithCompat), nameof(BeforeToolsmithCreatedTool)),
                postfix: new HarmonyMethod(typeof(ToolsmithCompat), nameof(AfterToolsmithCreatedTool)));
        }

        Type? recipeRegister = AccessTools.TypeByName("Toolsmith.RecipeRegisterModSystem");
        MethodInfo? assetsFinalize = recipeRegister == null
            ? null
            : AccessTools.Method(recipeRegister, nameof(ModSystem.AssetsFinalize), [typeof(ICoreAPI)]);
        if (assetsFinalize != null)
        {
            _harmony.Patch(assetsFinalize, postfix: new HarmonyMethod(typeof(ToolsmithCompat), nameof(AfterToolsmithRecipeRegisterAssetsFinalize)));
        }

        _disabledAssemblyBlockerApplied = true;
    }

    private static void PatchGenericDamageHandling()
    {
        if (_genericPatchesApplied) return;

        _harmony ??= new Harmony(HarmonyId);

        MethodInfo? modifyMeleeDamage = AccessTools.Method(typeof(WeaponBuffSystem), "ModifyMeleeDamage");
        if (modifyMeleeDamage != null)
        {
            _harmony.Patch(modifyMeleeDamage, postfix: new HarmonyMethod(typeof(ToolsmithCompat), nameof(AfterModifyMeleeDamage)));
        }

        _genericPatchesApplied = true;
    }

    public static void Unpatch()
    {
        if (_serverApi != null && _bleedTickListenerId != 0)
        {
            _serverApi.Event.UnregisterGameTickListener(_bleedTickListenerId);
        }

        _bleedTickListenerId = 0;
        _serverApi = null;
        _clientTinkeringChannel = null;
        _serverTinkeringChannel = null;
        if (_criticalChanceProviderRegistered)
        {
            GrindingWheelCompat.ExtraWeaponCriticalHitChance -= GetToolsmithCriticalHitChance;
        _criticalChanceProviderRegistered = false;
    }
    _genericPatchesApplied = false;
    _toolsmithPatchesApplied = false;
    _disabledAssemblyBlockerApplied = false;
    _toolsmithWeaponCraftingEnabled = false;
    _toolsmithWorkbenchAssemblyDepth = 0;
    Bleeds.Clear();
    KeepBaseWeaponVisualCache.Clear();
    _harmony?.UnpatchAll(HarmonyId);
    _harmony = null;
    }

    public static void AssetsFinalize(ICoreAPI api, bool toolsmithWeaponCraftingEnabled)
    {
        if (!api.ModLoader.IsModEnabled("toolsmith")) return;

        _toolsmithWeaponCraftingEnabled = toolsmithWeaponCraftingEnabled;
        if (_toolsmithWeaponCraftingEnabled)
        {
            AddTinkeringIngredientStorageFlags(api);
            RemoveToolsmithGeneratedWeaponGridRecipes(api);
            ApplyToolsmithOutputOverrides(api);
        }
        else
        {
            DisableToolsmithForCombatOverhaulWeapons(api);
        }
    }

    private static void AfterToolsmithRecipeRegisterAssetsFinalize(ICoreAPI api)
    {
        if (_toolsmithWeaponCraftingEnabled)
        {
            RemoveToolsmithGeneratedWeaponGridRecipes(api);
            ApplyToolsmithOutputOverrides(api);
        }
        else
        {
            DisableToolsmithForCombatOverhaulWeapons(api);
        }
    }

    private static void ApplyToolsmithOutputOverrides(ICoreAPI api)
    {
        ApplyKnownToolsmithHeadBehaviors(api);

        Type? recipeRegister = AccessTools.TypeByName("Toolsmith.RecipeRegisterModSystem");
        FieldInfo? recipesField = recipeRegister == null ? null : AccessTools.Field(recipeRegister, "TinkerToolGridRecipes");
        if (recipesField?.GetValue(null) is not IDictionary recipes) return;

        RegisterExactToolsmithOutputsFromGridRecipes(api, recipes);
        RemoveFinishedWeaponHeadMappingsAndBehaviors(api, recipes);

        string[] allMetals = ["copper", "tinbronze", "bismuthbronze", "blackbronze", "iron", "meteoriciron", "steel", "meteoricsteel", "springsteel"];
        string[] noCopper = ["tinbronze", "bismuthbronze", "blackbronze", "iron", "meteoriciron", "steel", "meteoricsteel", "springsteel"];
        string[] ironSteel = ["iron", "meteoriciron", "steel", "meteoricsteel", "springsteel"];
        string[] steelOnly = ["steel", "meteoricsteel", "springsteel"];
        string[] stoneAndMetal = ["chert", "granite", "andesite", "basalt", "obsidian", "peridotite", "flint", "copper", "tinbronze", "bismuthbronze", "blackbronze", "iron", "meteoriciron", "steel", "meteoricsteel", "springsteel"];

        SetToolsmithOutputFamily(recipes, api, "armory:part-shortsword", "armory:sword-short-plain", allMetals);
        SetToolsmithOutputFamily(recipes, api, "armory:part-armingsword", "armory:sword-arming-plain", ironSteel);
        SetToolsmithOutputFamily(recipes, api, "armory:part-longsword", "armory:sword-long-plain", noCopper);
        SetToolsmithOutputFamily(recipes, api, "armory:part-greatsword", "armory:sword-great-plain", ironSteel);
        SetToolsmithOutputFamily(recipes, api, "armory:part-sabre", "armory:sabre-plain", noCopper);
        SetToolsmithOutputFamily(recipes, api, "armory:part-dagger", "armory:dagger-long-plain", ironSteel);
        SetToolsmithOutputFamily(recipes, api, "armory:part-longaxehead", "armory:axe-long-plain", noCopper);
        SetToolsmithOutputFamily(recipes, api, "armory:part-javelinhead", "armory:javelin-plain", stoneAndMetal);
        SetToolsmithOutputFamily(recipes, api, "armory:part-macehead", "armory:mace-plain", stoneAndMetal);
        SetToolsmithOutputFamily(recipes, api, "armory:part-halberd", "armory:halberd-plain", ironSteel);
        SetToolsmithOutputFamily(recipes, api, "armory:part-poleaxehead", "armory:poleaxe-plain", steelOnly);
        SetToolsmithOutputFamily(recipes, api, "game:spearhead", "game:spear-generic", stoneAndMetal);
        SetToolsmithOutputFamily(recipes, api, "game:bladehead-falx", "game:blade-falx", allMetals);
        SetToolsmithOutputFamily(recipes, api, "armory:part-pikehead", "armory:pike-plain", noCopper);
        SetToolsmithOutputFamily(recipes, api, "armory:part-hammerhead", "armory:mace-battlehammer", steelOnly);
        SetToolsmithOutputFamily(recipes, api, "armory:part-battlehammerhead", "armory:mace-battlehammer", steelOnly);
        SetToolsmithOutputFamily(recipes, api, "armory:part-polehammerhead", "armory:club-polehammer", steelOnly);
        SetToolsmithOutputFamily(recipes, api, "armory:part-warhammerhead", "armory:mace-warhammer", steelOnly);
    }

    private static void DisableToolsmithForCombatOverhaulWeapons(ICoreAPI api)
    {
        RemoveToolsmithGeneratedWeaponGridRecipes(api);
        RemoveToolsmithWeaponRecipeMappings(api);
        RemoveToolsmithWeaponBehaviors(api);
    }

    private static void RemoveToolsmithGeneratedWeaponGridRecipes(ICoreAPI api)
    {
        List<GridRecipe> recipes = api.World.GridRecipes;

        for (int index = recipes.Count - 1; index >= 0; index--)
        {
            if (!IsToolsmithGeneratedWeaponGridRecipe(api, recipes[index])) continue;

            recipes.RemoveAt(index);
        }
    }

    private static bool IsToolsmithGeneratedWeaponGridRecipe(ICoreAPI api, GridRecipe recipe)
    {
        if (recipe.Output?.Code == null) return false;
        if (recipe.RecipeGroup != 2 || recipe.Width != 2 || recipe.Height != 2) return false;
        if (!string.Equals(recipe.IngredientPattern, "hb,r_", StringComparison.Ordinal)) return false;
        if (recipe.Ingredients == null
            || !recipe.Ingredients.ContainsKey("h")
            || !recipe.Ingredients.ContainsKey("b")
            || !recipe.Ingredients.ContainsKey("r"))
        {
            return false;
        }

        CollectibleObject? output = recipe.Output.ResolvedItemStack?.Collectible
            ?? (recipe.Output.Type == EnumItemClass.Block
                ? api.World.GetBlock(recipe.Output.Code)
                : api.World.GetItem(recipe.Output.Code));

        return ShouldKeepBaseWeaponVisual(output) || IsKnownToolsmithWeaponOutputCode(recipe.Output.Code);
    }

    private static void RemoveToolsmithWeaponRecipeMappings(ICoreAPI api)
    {
        Type? recipeRegister = AccessTools.TypeByName("Toolsmith.RecipeRegisterModSystem");
        FieldInfo? recipesField = recipeRegister == null ? null : AccessTools.Field(recipeRegister, "TinkerToolGridRecipes");
        if (recipesField?.GetValue(null) is not IDictionary recipes) return;

        List<object> keysToRemove = new();
        foreach (DictionaryEntry entry in recipes)
        {
            bool remove = entry.Value is CollectibleObject output && ShouldKeepBaseWeaponVisual(output);
            if (!remove && entry.Value is CollectibleObject outputWithCode)
            {
                remove = IsKnownToolsmithWeaponOutputCode(outputWithCode.Code);
            }
            if (!remove && entry.Key is string headCode)
            {
                remove = IsKnownToolsmithHeadCode(new AssetLocation(headCode))
                    || IsFinishedWeaponHeadMappingKey(api, headCode);
            }

            if (remove) keysToRemove.Add(entry.Key);
        }

        foreach (object key in keysToRemove)
        {
            recipes.Remove(key);
        }
    }

    private static void RemoveToolsmithWeaponBehaviors(ICoreAPI api)
    {
        Type? modularRenderingBehavior = AccessTools.TypeByName("Toolsmith.Client.Behaviors.ModularPartRenderingFromAttributes");
        Type? tinkeredToolsBehavior = AccessTools.TypeByName("Toolsmith.ToolTinkering.Behaviors.CollectibleBehaviorTinkeredTools");
        Type? toolHeadBehavior = AccessTools.TypeByName("Toolsmith.ToolTinkering.Behaviors.CollectibleBehaviorToolHead");
        Type? bluntBehavior = AccessTools.TypeByName("Toolsmith.ToolTinkering.Behaviors.CollectibleBehaviorToolBlunt");

        foreach (CollectibleObject? collectible in api.World.Collectibles)
        {
            if (collectible?.Code == null) continue;

            if (ShouldKeepBaseWeaponVisual(collectible) || IsKnownToolsmithWeaponOutputCode(collectible.Code))
            {
                RemoveCollectibleBehavior(collectible, modularRenderingBehavior);
                RemoveCollectibleBehavior(collectible, tinkeredToolsBehavior);
                RemoveCollectibleBehavior(collectible, bluntBehavior);
            }

            if (IsKnownToolsmithHeadCode(collectible.Code))
            {
                RemoveCollectibleBehavior(collectible, toolHeadBehavior);
                RemoveCollectibleBehavior(collectible, bluntBehavior);
            }
        }
    }

    private static bool IsKnownToolsmithHeadCode(AssetLocation? code)
    {
        if (code == null) return false;

        string domain = code.Domain;
        string path = code.Path;

        if (domain == "game")
        {
            return path.StartsWith("spearhead-", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("bladehead-falx-", StringComparison.OrdinalIgnoreCase);
        }

        if (domain != "armory") return false;

        return path.StartsWith("part-shortsword-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("part-armingsword-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("part-longsword-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("part-greatsword-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("part-sabre-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("part-dagger-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("part-longaxehead-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("part-javelinhead-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("part-macehead-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("part-hammerhead-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("part-battlehammerhead-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("part-polehammerhead-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("part-warhammerhead-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("part-halberd-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("part-pikehead-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("part-poleaxehead-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKnownToolsmithWeaponOutputCode(AssetLocation? code)
    {
        if (code == null) return false;

        string domain = code.Domain;
        string path = code.Path;

        if (domain == "game")
        {
            return path.StartsWith("spear-generic-", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("blade-falx-", StringComparison.OrdinalIgnoreCase);
        }

        if (domain != "armory") return false;

        return path.StartsWith("sword-short-plain-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("sword-arming-plain-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("sword-long-plain-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("sword-great-plain-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("sabre-plain-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("dagger-long-plain-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("axe-long-plain-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("javelin-plain-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("mace-plain-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("mace-battlehammer-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("mace-warhammer-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("club-plain-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("club-polehammer-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("halberd-plain-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("poleaxe-plain-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("pike-plain-", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyKnownToolsmithHeadBehaviors(ICoreAPI api)
    {
        string[] allMetals = ["copper", "tinbronze", "bismuthbronze", "blackbronze", "iron", "meteoriciron", "steel", "meteoricsteel", "springsteel"];
        string[] noCopper = ["tinbronze", "bismuthbronze", "blackbronze", "iron", "meteoriciron", "steel", "meteoricsteel", "springsteel"];
        string[] ironSteel = ["iron", "meteoriciron", "steel", "meteoricsteel", "springsteel"];
        string[] steelOnly = ["steel", "meteoricsteel", "springsteel"];
        string[] stoneAndMetal = ["chert", "granite", "andesite", "basalt", "obsidian", "peridotite", "flint", "copper", "tinbronze", "bismuthbronze", "blackbronze", "iron", "meteoriciron", "steel", "meteoricsteel", "springsteel"];

        AddKnownToolsmithHeadFamily(api, "armory:part-shortsword", "armory:sword-short-plain", allMetals);
        AddKnownToolsmithHeadFamily(api, "armory:part-armingsword", "armory:sword-arming-plain", ironSteel);
        AddKnownToolsmithHeadFamily(api, "armory:part-longsword", "armory:sword-long-plain", noCopper);
        AddKnownToolsmithHeadFamily(api, "armory:part-greatsword", "armory:sword-great-plain", ironSteel);
        AddKnownToolsmithHeadFamily(api, "armory:part-sabre", "armory:sabre-plain", noCopper);
        AddKnownToolsmithHeadFamily(api, "armory:part-dagger", "armory:dagger-long-plain", ironSteel);
        AddKnownToolsmithHeadFamily(api, "armory:part-longaxehead", "armory:axe-long-plain", noCopper);
        AddKnownToolsmithHeadFamily(api, "armory:part-javelinhead", "armory:javelin-plain", stoneAndMetal);
        AddKnownToolsmithHeadFamily(api, "armory:part-macehead", "armory:mace-plain", stoneAndMetal);
        AddKnownToolsmithHeadFamily(api, "armory:part-halberd", "armory:halberd-plain", ironSteel);
        AddKnownToolsmithHeadFamily(api, "armory:part-poleaxehead", "armory:poleaxe-plain", steelOnly);
        AddKnownToolsmithHeadFamily(api, "game:spearhead", "game:spear-generic", stoneAndMetal);
        AddKnownToolsmithHeadFamily(api, "game:bladehead-falx", "game:blade-falx", allMetals);
        AddKnownToolsmithHeadFamily(api, "armory:part-pikehead", "armory:pike-plain", noCopper);
        AddKnownToolsmithHeadFamily(api, "armory:part-hammerhead", "armory:mace-battlehammer", steelOnly);
        AddKnownToolsmithHeadFamily(api, "armory:part-battlehammerhead", "armory:mace-battlehammer", steelOnly);
        AddKnownToolsmithHeadFamily(api, "armory:part-polehammerhead", "armory:club-polehammer", steelOnly);
        AddKnownToolsmithHeadFamily(api, "armory:part-warhammerhead", "armory:mace-warhammer", steelOnly);
    }

    private static void AddKnownToolsmithHeadFamily(ICoreAPI api, string headPrefix, string outputPrefix, IEnumerable<string> materials)
    {
        foreach (string material in materials)
        {
            TryAddToolsmithHeadBehaviorForOutput(api, $"{headPrefix}-{material}", $"{outputPrefix}-{material}");
        }
    }

    private static void SetToolsmithOutputFamily(IDictionary recipes, ICoreAPI api, string headPrefix, string outputPrefix, IEnumerable<string> materials)
    {
        foreach (string material in materials)
        {
            SetToolsmithOutput(recipes, api, $"{headPrefix}-{material}", $"{outputPrefix}-{material}");
        }
    }

    private static void RegisterExactToolsmithOutputsFromGridRecipes(ICoreAPI api, IDictionary recipes)
    {
        Type? configUtility = AccessTools.TypeByName("Toolsmith.Utils.ConfigUtility");
        MethodInfo? isToolHead = configUtility == null ? null : AccessTools.Method(configUtility, "IsToolHead", [typeof(string)]);
        if (isToolHead == null) return;

        foreach (GridRecipe recipe in api.World.GridRecipes)
        {
            string? outputCode = recipe.Output?.Code?.ToString();
            if (string.IsNullOrWhiteSpace(outputCode) || recipe.Ingredients == null) continue;

            foreach (CraftingRecipeIngredient? ingredient in recipe.Ingredients.Values)
            {
                if (ingredient == null) continue;

                string? headCode = ingredient.Code?.ToString();
                if (string.IsNullOrWhiteSpace(headCode) || IsToolsmithToolHead(isToolHead, headCode) != true) continue;

                foreach ((string exactHeadCode, string exactOutputCode) in ExpandToolsmithHeadRecipeCodes(ingredient, outputCode))
                {
                    SetToolsmithOutput(recipes, api, exactHeadCode, exactOutputCode);
                }
            }
        }
    }

    private static bool IsToolsmithToolHead(MethodInfo isToolHead, string code)
    {
        return isToolHead.Invoke(null, [code]) is bool result && result;
    }

    private static IEnumerable<(string HeadCode, string OutputCode)> ExpandToolsmithHeadRecipeCodes(CraftingRecipeIngredient ingredient, string outputCode)
    {
        string? headCode = ingredient.Code?.ToString();
        if (string.IsNullOrWhiteSpace(headCode)) yield break;

        if (!headCode.Contains('*') && !outputCode.Contains('{'))
        {
            yield return (headCode, outputCode);
            yield break;
        }

        string? variantName = ingredient.Name;
        string[]? variants = ingredient.AllowedVariants;
        if (string.IsNullOrWhiteSpace(variantName) || variants == null || variants.Length == 0) yield break;

        HashSet<string> skipped = ingredient.SkipVariants == null
            ? new(StringComparer.OrdinalIgnoreCase)
            : new(ingredient.SkipVariants, StringComparer.OrdinalIgnoreCase);
        string outputToken = "{" + variantName + "}";

        foreach (string variant in variants)
        {
            if (string.IsNullOrWhiteSpace(variant) || skipped.Contains(variant)) continue;

            string exactHeadCode = headCode.Replace("*", variant);
            string exactOutputCode = outputCode.Replace(outputToken, variant);
            if (exactHeadCode.Contains('*') || exactOutputCode.Contains('{')) continue;

            yield return (exactHeadCode, exactOutputCode);
        }
    }

    private static void SetToolsmithOutput(IDictionary recipes, ICoreAPI api, string headCode, string outputCode)
    {
        if (IsBlockedToolsmithHeadCode(api, headCode)) return;

        CollectibleObject? output = api.World.GetItem(new AssetLocation(outputCode));
        if (output == null || !ShouldKeepBaseWeaponVisual(output)) return;

        recipes[headCode] = output;
        TryAddToolsmithOutputBehavior(output);
        TryAddToolsmithHeadBehavior(api, headCode, output);
    }

    private static void TryAddToolsmithHeadBehavior(ICoreAPI api, string headCode, CollectibleObject output)
    {
        if (IsBlockedToolsmithHeadCode(api, headCode)) return;

        Item? head = api.World.GetItem(new AssetLocation(headCode));
        if (head == null) return;

        Type? toolHeadBehavior = AccessTools.TypeByName("Toolsmith.ToolTinkering.Behaviors.CollectibleBehaviorToolHead");
        AddCollectibleBehavior(head, toolHeadBehavior);
        RemoveToolsmithBluntBehavior(head);
    }

    private static void RemoveFinishedWeaponHeadMappingsAndBehaviors(ICoreAPI api, IDictionary recipes)
    {
        List<object> keysToRemove = new();
        foreach (DictionaryEntry entry in recipes)
        {
            if (entry.Key is string headCode && IsFinishedWeaponHeadMappingKey(api, headCode))
            {
                keysToRemove.Add(entry.Key);
            }
        }

        foreach (object key in keysToRemove)
        {
            recipes.Remove(key);
        }

        Type? toolHeadBehavior = AccessTools.TypeByName("Toolsmith.ToolTinkering.Behaviors.CollectibleBehaviorToolHead");
        foreach (Item? item in api.World.Items)
        {
            if (item?.Code == null || !IsFinishedWeaponHeadCode(api, item.Code.ToString())) continue;

            RemoveCollectibleBehavior(item, toolHeadBehavior);
        }
    }

    private static bool IsFinishedWeaponHeadMappingKey(ICoreAPI api, string headCode)
    {
        if (IsFinishedWeaponHeadCode(api, headCode)) return true;

        const string boneHandleSuffix = "-bone";
        return headCode.EndsWith(boneHandleSuffix, StringComparison.OrdinalIgnoreCase)
            && IsFinishedWeaponHeadCode(api, headCode[..^boneHandleSuffix.Length]);
    }

    private static bool IsBlockedToolsmithHeadCode(ICoreAPI api, string headCode)
    {
        return IsInvalidArmoryArmingSwordHead(headCode)
            || IsVanillaKnifeBladeHeadCode(headCode)
            || IsFinishedWeaponHeadCode(api, headCode);
    }

    private static bool IsBlockedToolsmithHeadCode(ICoreAPI api, AssetLocation code)
    {
        return IsBlockedToolsmithHeadCode(api, code.ToString());
    }

    private static bool IsFinishedWeaponHeadCode(ICoreAPI api, string headCode)
    {
        AssetLocation code = new(headCode);
        if (IsKnownToolsmithWeaponOutputCode(code)) return true;

        CollectibleObject? collectible = api.World.GetItem(code);
        return ShouldKeepBaseWeaponVisual(collectible);
    }

    private static bool IsInvalidArmoryArmingSwordHead(string headCode)
    {
        return headCode.Equals("armory:part-armingsword-copper", StringComparison.OrdinalIgnoreCase)
            || headCode.Equals("armory:part-armingsword-tinbronze", StringComparison.OrdinalIgnoreCase)
            || headCode.Equals("armory:part-armingsword-bismuthbronze", StringComparison.OrdinalIgnoreCase)
            || headCode.Equals("armory:part-armingsword-blackbronze", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVanillaKnifeBladeHeadCode(string headCode)
    {
        AssetLocation code = new(headCode);
        return code.Domain.Equals("game", StringComparison.OrdinalIgnoreCase)
            && code.Path.StartsWith("knifeblade-", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryAddToolsmithHeadBehaviorForOutput(ICoreAPI api, string headCode, string outputCode)
    {
        if (IsBlockedToolsmithHeadCode(api, headCode)) return;

        CollectibleObject? output = api.World.GetItem(new AssetLocation(outputCode));
        if (output == null || !ShouldKeepBaseWeaponVisual(output)) return;

        TryAddToolsmithOutputBehavior(output);
        TryAddToolsmithHeadBehavior(api, headCode, output);
    }

    private static void TryAddToolsmithOutputBehavior(CollectibleObject output)
    {
        Type? modularRenderingBehavior = AccessTools.TypeByName("Toolsmith.Client.Behaviors.ModularPartRenderingFromAttributes");
        Type? tinkeredToolsBehavior = AccessTools.TypeByName("Toolsmith.ToolTinkering.Behaviors.CollectibleBehaviorTinkeredTools");
        AddCollectibleBehavior(output, modularRenderingBehavior);
        AddCollectibleBehavior(output, tinkeredToolsBehavior);
        RemoveToolsmithBluntBehavior(output);
    }

    private static void AddCollectibleBehavior(CollectibleObject collectible, Type? behaviorType)
    {
        if (behaviorType == null || collectible.CollectibleBehaviors?.Any(behavior => behavior.GetType() == behaviorType) == true) return;

        if (Activator.CreateInstance(behaviorType, collectible) is not CollectibleBehavior behavior) return;

        collectible.CollectibleBehaviors = (collectible.CollectibleBehaviors ?? []).Append(behavior).ToArray();
    }

    private static void RemoveCollectibleBehavior(CollectibleObject collectible, Type? behaviorType)
    {
        if (behaviorType == null || collectible.CollectibleBehaviors == null) return;

        CollectibleBehavior[] behaviors = collectible.CollectibleBehaviors
            .Where(behavior => behavior.GetType() != behaviorType)
            .ToArray();
        if (behaviors.Length == collectible.CollectibleBehaviors.Length) return;

        collectible.CollectibleBehaviors = behaviors;
    }

    private static void RemoveToolsmithBluntBehavior(CollectibleObject collectible)
    {
        Type? bluntBehaviorType = AccessTools.TypeByName("Toolsmith.ToolTinkering.Behaviors.CollectibleBehaviorToolBlunt");
        if (bluntBehaviorType == null || collectible.CollectibleBehaviors == null) return;

        CollectibleBehavior[] behaviors = collectible.CollectibleBehaviors
            .Where(behavior => behavior.GetType() != bluntBehaviorType)
            .ToArray();
        if (behaviors.Length == collectible.CollectibleBehaviors.Length) return;

        collectible.CollectibleBehaviors = behaviors;
    }

    private static void AddTinkeringIngredientStorageFlags(ICoreAPI api)
    {
        foreach (Item? item in api.World.Items)
        {
            AssetLocation? code = item?.Code;
            if (item == null || code == null) continue;

            string path = code.Path;
            if (IsSaw(path) || IsChisel(path) || TryGetNailMaterial(path, out _))
            {
                item.StorageFlags |= EnumItemStorageFlags.Offhand;
            }
        }
    }

    private static void RegisterTinkeringNetwork(ICoreAPI api)
    {
        if (api is ICoreClientAPI clientApi && _clientTinkeringChannel == null)
        {
            _clientTinkeringChannel = clientApi.Network
                .RegisterChannel(ToolsmithTinkeringNetworkChannel)
                .RegisterMessageType<ToolsmithTinkeringPacket>();
        }

        if (api is ICoreServerAPI serverApi && _serverTinkeringChannel == null)
        {
            _serverTinkeringChannel = serverApi.Network
                .RegisterChannel(ToolsmithTinkeringNetworkChannel)
                .RegisterMessageType<ToolsmithTinkeringPacket>()
                .SetMessageHandler<ToolsmithTinkeringPacket>(HandleTinkeringPacket);
        }
    }

    private static void RegisterServerState(ICoreAPI api)
    {
        if (api is not ICoreServerAPI serverApi || _serverApi != null) return;

        _serverApi = serverApi;
        _bleedTickListenerId = serverApi.Event.RegisterGameTickListener(TickBleeds, 1000, 1000);
    }

    private static void RegisterCriticalChanceProvider(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Server || _criticalChanceProviderRegistered) return;

        GrindingWheelCompat.ExtraWeaponCriticalHitChance += GetToolsmithCriticalHitChance;
        _criticalChanceProviderRegistered = true;
    }

    private static void ApplyToolsmithCreatedWeapon(ItemStack stack)
    {
        ApplyToolsmithAttackSpeed(stack);

        stack.Attributes.RemoveAttribute(ModularMultiPartRenderDataAttribute);

        AssetLocation? shapeBase = stack.Item?.Shape?.Base;
        if (shapeBase == null) return;

        TreeAttribute renderTree = new();
        renderTree.SetString(PartShapeIndexAttribute, $"{shapeBase.Domain}:shapes/{shapeBase.Path}");
        stack.Attributes[ModularPartRenderDataAttribute] = renderTree;
        stack.TempAttributes.RemoveAttribute(ToolsmithMeshRefIdAttribute);
    }

    private static void ApplyToolsmithAttackSpeed(ItemStack stack)
    {
        WeaponBuffSystem.Current?.RemoveBuff(stack, ToolsmithAttackSpeedBuffCode, ToolsmithAttackSpeedBuffSource);

        float speedBonus = stack.Attributes.GetFloat(ToolsmithSpeedBonusAttribute, 0f);
        if (MathF.Abs(speedBonus) <= 0.0001f)
        {
            RemoveAttackSpeedFallback(stack);
            return;
        }

        float multiplier = MathF.Max(0.01f, 1f + speedBonus);
        ApplyAttackSpeedFallback(stack, speedBonus, multiplier);
    }

    private static void AfterWeaponBuffTooltip(ItemStack? stack, StringBuilder description, IWorldAccessor world, bool withDebugInfo)
    {
        AppendToolsmithModifierTooltip(stack, description);
    }

    private static void AfterCollectibleHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        AppendToolsmithModifierTooltip(inSlot.Itemstack, dsc);
    }

    private static void AppendToolsmithModifierTooltip(ItemStack? stack, StringBuilder description)
    {
        EnsureToolsmithWeaponDurabilityReduced(stack);
        if (!IsToolsmithCompatAssembledWeapon(stack) || stack == null) return;

        RemoveLegacyAttackSpeedBuffTooltip(description);

        float speedBonus = stack.Attributes.GetFloat(ToolsmithAttackSpeedFallbackAttribute, stack.Attributes.GetFloat(ToolsmithSpeedBonusAttribute, 0f));
        if (MathF.Abs(speedBonus) > 0.0001f)
        {
            AppendTooltipLineOnce(description, $"<font color=\"{AttackSpeedTooltipColor}\">Attack Speed: {FormatSignedPercent(speedBonus)}</font>");
        }

        float critChance = GetToolsmithSharpnessCritChance(stack);
        if (HasToolsmithSharpness(stack) && critChance >= 0f)
        {
            AppendTooltipLineOnce(description, $"<font color=\"{CritChanceTooltipColor}\">Crit Chance: {FormatSignedPercent(critChance)}</font>");
        }

        if (stack.Attributes.GetBool(SerratedAttribute, false))
        {
            float bleedDamagePerSecond = GetSerratedTooltipBleedDamagePerSecond(stack);
            AppendTooltipLineOnce(description, $"<font color=\"{SerratedTooltipColor}\">Serrated: {FormatDamage(bleedDamagePerSecond)} slash damage per second for {FormatDuration(SerratedBleedDurationSeconds)} seconds</font>");
        }

        int nailMaxDurability = stack.Attributes.GetInt(NailMaxDurabilityAttribute, 0);
        if (nailMaxDurability > 0)
        {
            int nailCurrentDurability = Math.Clamp(stack.Attributes.GetInt(NailCurrentDurabilityAttribute, 0), 0, nailMaxDurability);
            AppendTooltipLineOnce(description, $"<font color=\"{NailTooltipColor}\">Studded: +{FormatDamage(NailPierceDamage)} Pierce damage on hit. {nailCurrentDurability}/{nailMaxDurability}</font>");
        }

        int needleArmorPiercing = stack.Attributes.GetInt(NeedlePointArmorPiercingAppliedAttribute, 0);
        if (stack.Attributes.GetBool(NeedlePointAttribute, false) || needleArmorPiercing > 0)
        {
            AppendTooltipLineOnce(description, $"<font color=\"{ArmorPiercingTooltipColor}\">Armor Piercing: +{Math.Max(needleArmorPiercing, NeedlePointArmorPiercingBonus)}</font>");
        }
    }

    private static void AfterModifyMeleeDamage(Entity target, DamageSource damageSource, ItemSlot? slot, ref float damage)
    {
        ItemStack? stack = slot?.Itemstack;
        if (stack == null || slot == null || damage <= 0f) return;

        if (!IsToolsmithCompatAssembledWeapon(stack)) return;

        if (damageSource.Type == EnumDamageType.SlashingAttack && stack.Attributes.GetBool(SerratedAttribute, false))
        {
            ApplySerratedBleed(target, damageSource, damage);
        }

        if (damageSource.Type == EnumDamageType.BluntAttack)
        {
            TryApplyNailPierceDamage(target, damageSource, slot, stack);
        }
    }

    private static float GetToolsmithCriticalHitChance(ItemStack weaponStack, Entity target, float damage)
    {
        return IsToolsmithCompatAssembledWeapon(weaponStack) ? GetToolsmithSharpnessCritChance(weaponStack) : 0f;
    }

    private static bool BeforeHeldUseStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, EnumHandInteract useType, bool firstEvent, ref EnumHandHandling handling)
    {
        if (useType != EnumHandInteract.HeldItemInteract) return true;
        if (!TryGetTinkeringAction(slot, byEntity, out TinkeringAction action)) return true;

        if (firstEvent)
        {
            SetActiveTinkeringAction(slot.Itemstack, action);
            if (byEntity.World.Side == EnumAppSide.Server)
            {
                PlayTinkeringSound(byEntity);
            }
        }

        byEntity.StartAnimation("craftingwinding");
        handling = EnumHandHandling.PreventDefault;
        return false;
    }

    private static bool BeforeHeldUseStep(ref EnumHandInteract __result, float secondsPassed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
    {
        if (!TryGetActiveTinkeringAction(slot.Itemstack, out _)) return true;

        __result = secondsPassed < ToolsmithTinkeringDurationSeconds
            ? byEntity.Controls.HandUse
            : EnumHandInteract.None;
        return false;
    }

    private static bool BeforeHeldUseStop(float secondsPassed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, EnumHandInteract useType)
    {
        if (useType != EnumHandInteract.HeldItemInteract || !TryGetActiveTinkeringAction(slot.Itemstack, out _)) return true;

        ClearActiveTinkeringAction(slot.Itemstack);
        byEntity.StopAnimation("craftingwinding");

        if (byEntity.World.Side == EnumAppSide.Server
            && secondsPassed >= ToolsmithTinkeringDurationSeconds - ToolsmithTinkeringCompletionGraceSeconds
            && TryGetTinkeringAction(slot, byEntity, out TinkeringAction action))
        {
            ApplyTinkeringAction(slot, byEntity, action);
        }

        return false;
    }

    private static bool BeforeHeldUseCancel(ref EnumHandInteract __result, float secondsPassed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, EnumItemUseCancelReason cancelReason)
    {
        if (!TryGetActiveTinkeringAction(slot.Itemstack, out _)) return true;

        ClearActiveTinkeringAction(slot.Itemstack);
        byEntity.StopAnimation("craftingwinding");
        __result = EnumHandInteract.None;
        return false;
    }

    private static bool BeforeCombatOverhaulActionEvent(ActionsManagerPlayerBehavior __instance, ActionEventData eventData, int itemId, ref bool __result)
    {
        if (_clientTinkeringChannel == null || eventData.Action.Action != EnumEntityAction.RightMouseDown) return true;

        EntityPlayer? player = GetActionsManagerPlayer(__instance);
        if (player == null) return true;

        ItemSlot mainHandSlot = GetMainHandSlot(player);
        if (mainHandSlot.Itemstack?.Item?.Id != itemId) return true;

        bool handled = eventData.Action.State switch
        {
            ActionState.Pressed or ActionState.Active or ActionState.Hold => HandleClientTinkeringHold(mainHandSlot, player),
            ActionState.Released or ActionState.Inactive => HandleClientTinkeringRelease(mainHandSlot, player),
            _ => false
        };

        if (!handled) return true;

        __result = true;
        return false;
    }

    private static bool HandleClientTinkeringHold(ItemSlot slot, EntityPlayer player)
    {
        ItemStack? stack = slot.Itemstack;
        if (stack == null) return false;

        bool hasActiveAction = TryGetActiveTinkeringAction(stack, out TinkeringAction activeAction);
        if (!TryGetTinkeringAction(slot, player, out TinkeringAction currentAction))
        {
            if (hasActiveAction) StopClientTinkering(slot, player);
            return hasActiveAction;
        }

        if (hasActiveAction && !SameTinkeringAction(activeAction, currentAction))
        {
            StopClientTinkering(slot, player);
            hasActiveAction = false;
        }

        if (!hasActiveAction)
        {
            StartClientTinkering(slot, player, currentAction);
            return true;
        }

        ContinueClientTinkering(slot, player, currentAction);
        return true;
    }

    private static bool HandleClientTinkeringRelease(ItemSlot slot, EntityPlayer player)
    {
        if (!TryGetActiveTinkeringAction(slot.Itemstack, out _)) return false;

        StopClientTinkering(slot, player);
        return true;
    }

    private static void StartClientTinkering(ItemSlot slot, EntityPlayer player, TinkeringAction action)
    {
        ItemStack? stack = slot.Itemstack;
        if (stack == null) return;

        SetActiveTinkeringAction(stack, action, player.World.ElapsedMilliseconds);
        player.StartAnimation("craftingwinding");
        PlayTinkeringSound(player);
    }

    private static void ContinueClientTinkering(ItemSlot slot, EntityPlayer player, TinkeringAction action)
    {
        ItemStack? stack = slot.Itemstack;
        if (stack == null) return;

        long nowMs = player.World.ElapsedMilliseconds;
        long lastMs = stack.TempAttributes.GetLong(ActiveTinkeringLastMsAttribute, nowMs);
        if (lastMs > 0 && nowMs - lastMs > ActiveTinkeringInputGapMilliseconds)
        {
            SetActiveTinkeringAction(stack, action, nowMs);
            player.StartAnimation("craftingwinding");
            return;
        }

        stack.TempAttributes.SetLong(ActiveTinkeringLastMsAttribute, nowMs);
        if (stack.TempAttributes.GetBool(ActiveTinkeringSubmittedAttribute, false)) return;

        long startedMs = stack.TempAttributes.GetLong(ActiveTinkeringStartedMsAttribute, nowMs);
        if ((nowMs - startedMs) / 1000f < ToolsmithTinkeringDurationSeconds) return;

        stack.TempAttributes.SetBool(ActiveTinkeringSubmittedAttribute, true);
        _clientTinkeringChannel?.SendPacket(new ToolsmithTinkeringPacket
        {
            Action = action.Type.ToString(),
            Material = action.Material
        });
        player.StopAnimation("craftingwinding");
    }

    private static void StopClientTinkering(ItemSlot slot, EntityPlayer player)
    {
        ClearActiveTinkeringAction(slot.Itemstack);
        player.StopAnimation("craftingwinding");
    }

    private static void HandleTinkeringPacket(IServerPlayer player, ToolsmithTinkeringPacket packet)
    {
        EntityPlayer? entity = player.Entity;
        if (entity == null) return;

        ItemSlot mainHandSlot = GetMainHandSlot(entity);
        if (!TryGetTinkeringAction(mainHandSlot, entity, out TinkeringAction action)) return;
        if (!string.Equals(packet.Action ?? "", action.Type.ToString(), StringComparison.Ordinal)) return;
        if (!string.Equals(packet.Material ?? "", action.Material, StringComparison.OrdinalIgnoreCase)) return;

        ApplyTinkeringAction(mainHandSlot, entity, action);
    }

    private static EntityPlayer? GetActionsManagerPlayer(ActionsManagerPlayerBehavior behavior)
    {
        return ActionsManagerPlayerField?.GetValue(behavior) as EntityPlayer;
    }

    private static ItemSlot GetMainHandSlot(EntityPlayer player)
    {
        ItemSlot? active = player.ActiveHandItemSlot;
        if (active?.Itemstack != null) return active;

        ItemSlot? right = player.RightHandItemSlot;
        if (right?.Itemstack != null) return right;

        return active ?? right ?? player.LeftHandItemSlot;
    }

    private static bool SameTinkeringAction(TinkeringAction left, TinkeringAction right)
    {
        return left.Type == right.Type && string.Equals(left.Material, right.Material, StringComparison.OrdinalIgnoreCase);
    }

    private static void AfterGetMaxDurability(ItemStack itemstack, ref int __result)
    {
        if (!IsToolsmithCompatAssembledWeapon(itemstack) || __result <= 0) return;

        float durabilityFactor = IsToolsmithDurabilityReductionSuppressed(itemstack) ? 1f : ToolsmithWeaponDurabilityFactor;
        durabilityFactor *= GetTinkeringDurabilityFactor(itemstack);
        if (MathF.Abs(durabilityFactor - 1f) <= 0.0001f) return;

        __result = Math.Max(1, (int)MathF.Round(__result * durabilityFactor));
    }

    private static bool TryGetTinkeringAction(ItemSlot slot, EntityAgent byEntity, out TinkeringAction action)
    {
        action = new(TinkeringActionType.None);

        ItemStack? stack = slot.Itemstack;
        if (!IsToolsmithCompatAssembledWeapon(stack) || stack == null) return false;
        if (EnsureToolsmithWeaponDurabilityReduced(stack)) slot.MarkDirty();

        ItemSlot? offhandSlot = byEntity.LeftHandItemSlot;
        ItemStack? offhandStack = offhandSlot?.Itemstack;
        AssetLocation? offhandCode = offhandStack?.Collectible?.Code;
        if (offhandSlot == null || offhandStack == null || offhandCode == null || ReferenceEquals(slot, offhandSlot)) return false;

        string offhandPath = offhandCode.Path;

        if (IsSaw(offhandPath)
            && !stack.Attributes.GetBool(SerratedAttribute, false)
            && WeaponHighestConfiguredDamageTypeIs(stack, EnumDamageType.SlashingAttack))
        {
            action = new(TinkeringActionType.Serrate);
            return true;
        }

        if (TryGetNailMaterial(offhandPath, out string nailMaterial)
            && stack.Attributes.GetInt(NailCurrentDurabilityAttribute, 0) <= 0
            && WeaponHighestConfiguredDamageTypeIs(stack, EnumDamageType.BluntAttack))
        {
            action = new(TinkeringActionType.AttachNails, nailMaterial);
            return true;
        }

        if (IsChisel(offhandPath)
            && !stack.Attributes.GetBool(NeedlePointAttribute, false)
            && WeaponHighestConfiguredDamageTypeIs(stack, EnumDamageType.PiercingAttack))
        {
            action = new(TinkeringActionType.NeedlePoint);
            return true;
        }

        return false;
    }

    private static void ApplyTinkeringAction(ItemSlot slot, EntityAgent byEntity, TinkeringAction action)
    {
        ItemStack? stack = slot.Itemstack;
        if (!IsToolsmithCompatAssembledWeapon(stack) || stack == null) return;

        switch (action.Type)
        {
            case TinkeringActionType.Serrate:
                int currentHeadDurabilityBeforeSerration = GetCurrentHeadDurability(stack);
                stack.Attributes.SetBool(SerratedAttribute, true);
                ScaleCurrentHeadDurability(stack, currentHeadDurabilityBeforeSerration, SerratedDurabilityFactor);
                DamageOffhandTool(byEntity);
                break;
            case TinkeringActionType.AttachNails:
                int nailMaxDurability = GetNailMaxDurability(action.Material);
                stack.Attributes.SetString(NailMaterialAttribute, action.Material);
                stack.Attributes.SetInt(NailMaxDurabilityAttribute, nailMaxDurability);
                stack.Attributes.SetInt(NailCurrentDurabilityAttribute, nailMaxDurability);
                ConsumeOffhandItem(byEntity);
                break;
            case TinkeringActionType.NeedlePoint:
                int currentHeadDurabilityBeforeNeedlePoint = GetCurrentHeadDurability(stack);
                stack.Attributes.SetBool(NeedlePointAttribute, true);
                ApplyNeedlePointArmorPiercing(stack);
                ScaleCurrentHeadDurability(stack, currentHeadDurabilityBeforeNeedlePoint, NeedlePointDurabilityFactor);
                DamageOffhandTool(byEntity);
                break;
        }

        slot.MarkDirty();
    }

    private static void ApplyNeedlePointArmorPiercing(ItemStack stack)
    {
        if (stack.Attributes.GetInt(NeedlePointArmorPiercingAppliedAttribute, 0) > 0) return;

        int armorPiercingBonus = stack.Attributes.GetInt(WeaponBuffStatCodes.ArmorPiercingBonus, 0);
        stack.Attributes.SetInt(WeaponBuffStatCodes.ArmorPiercingBonus, armorPiercingBonus + NeedlePointArmorPiercingBonus);
        stack.Attributes.SetInt(NeedlePointArmorPiercingAppliedAttribute, NeedlePointArmorPiercingBonus);
    }

    private static int GetCurrentHeadDurability(ItemStack stack)
    {
        return stack.Collectible?.GetRemainingDurability(stack) ?? 0;
    }

    private static bool EnsureToolsmithWeaponDurabilityReduced(ItemStack? stack)
    {
        if (!IsToolsmithCompatAssembledWeapon(stack) || stack == null) return false;
        if (IsToolsmithDurabilityReductionSuppressed(stack)) return false;
        if (stack.Attributes.GetBool(ToolsmithDurabilityReducedAttribute, false)) return false;

        stack.Attributes.SetBool(ToolsmithDurabilityReducedAttribute, true);

        ReduceToolsmithHeadDurability(stack);
        ScaleDurabilityAttributePair(stack, ToolsmithToolHandleCurrentDurabilityAttribute, ToolsmithToolHandleMaxDurabilityAttribute);
        ScaleDurabilityAttributePair(stack, ToolsmithToolBindingCurrentDurabilityAttribute, ToolsmithToolBindingMaxDurabilityAttribute);

        return true;
    }

    private static void ReduceToolsmithHeadDurability(ItemStack stack)
    {
        if (stack.Collectible == null) return;

        int reducedMaxDurability = stack.Collectible.GetMaxDurability(stack);
        if (reducedMaxDurability <= 0) return;

        if (!stack.Attributes.HasAttribute(ToolsmithToolDurabilityAttribute))
        {
            stack.Collectible.SetDurability(stack, reducedMaxDurability);
            return;
        }

        int currentDurability = stack.Collectible.GetRemainingDurability(stack);
        if (currentDurability <= 0) return;

        int reducedCurrentDurability = Math.Clamp((int)MathF.Round(currentDurability * ToolsmithWeaponDurabilityFactor), 1, reducedMaxDurability);
        stack.Collectible.SetDurability(stack, reducedCurrentDurability);
    }

    private static bool IsToolsmithDurabilityReductionSuppressed(ItemStack stack)
    {
        return stack.TempAttributes.GetBool(SuppressToolsmithDurabilityReductionAttribute, false);
    }

    private static void ScaleDurabilityAttributePair(ItemStack stack, string currentAttribute, string maxAttribute)
    {
        int maxDurability = stack.Attributes.GetInt(maxAttribute, 0);
        if (maxDurability <= 0) return;

        int currentDurability = stack.Attributes.GetInt(currentAttribute, maxDurability);
        int reducedMaxDurability = Math.Max(1, (int)MathF.Round(maxDurability * ToolsmithWeaponDurabilityFactor));
        int reducedCurrentDurability = currentDurability <= 0
            ? 0
            : Math.Clamp((int)MathF.Round(currentDurability * ToolsmithWeaponDurabilityFactor), 1, reducedMaxDurability);

        stack.Attributes.SetInt(maxAttribute, reducedMaxDurability);
        stack.Attributes.SetInt(currentAttribute, reducedCurrentDurability);
    }

    private static void ScaleCurrentHeadDurability(ItemStack stack, int currentDurability, float factor)
    {
        if (stack.Collectible == null || factor >= 0.999f) return;
        if (currentDurability <= 0) return;

        int newMaxDurability = stack.Collectible.GetMaxDurability(stack);
        int newCurrentDurability = Math.Clamp((int)MathF.Floor(currentDurability * factor), 1, Math.Max(1, newMaxDurability));
        stack.Collectible.SetDurability(stack, newCurrentDurability);
    }

    private static float GetTinkeringDurabilityFactor(ItemStack stack)
    {
        float factor = 1f;
        if (stack.Attributes.GetBool(SerratedAttribute, false)) factor *= SerratedDurabilityFactor;
        if (stack.Attributes.GetBool(NeedlePointAttribute, false)) factor *= NeedlePointDurabilityFactor;
        return factor;
    }

    private static void TryApplyNailPierceDamage(Entity target, DamageSource damageSource, ItemSlot slot, ItemStack stack)
    {
        int nailCurrentDurability = stack.Attributes.GetInt(NailCurrentDurabilityAttribute, 0);
        if (nailCurrentDurability <= 0) return;

        DamageSource nailDamageSource = CreateNailDamageSource(damageSource, stack);
        target.ReceiveDamage(nailDamageSource, NailPierceDamage);

        stack.Attributes.SetInt(NailCurrentDurabilityAttribute, Math.Max(0, nailCurrentDurability - 1));
        slot.MarkDirty();
    }

    private static DamageSource CreateNailDamageSource(DamageSource source, ItemStack stack)
    {
        DamageData damageData = new(EnumDamageType.PiercingAttack, Math.Max(0, source.DamageTier), NailArmorPiercingBonus);

        if (source is DirectionalTypedDamageSource directionalSource)
        {
            return new DirectionalTypedDamageSource
            {
                Source = source.Source,
                SourceEntity = source.SourceEntity,
                CauseEntity = source.CauseEntity,
                DamageTypeData = damageData,
                Position = directionalSource.Position,
                Collider = directionalSource.Collider,
                KnockbackStrength = 0,
                DamageTier = damageData.Tier,
                Type = EnumDamageType.PiercingAttack,
                Weapon = stack,
                IgnoreInvFrames = true
            };
        }

        return new TypedDamageSource
        {
            Source = source.Source,
            SourceEntity = source.SourceEntity,
            CauseEntity = source.CauseEntity,
            DamageTypeData = damageData,
            KnockbackStrength = 0,
            DamageTier = damageData.Tier,
            Type = EnumDamageType.PiercingAttack,
            Weapon = stack,
            IgnoreInvFrames = true
        };
    }

    private static void ApplySerratedBleed(Entity target, DamageSource damageSource, float sourceDamage)
    {
        if (_serverApi == null || target.World.Side != EnumAppSide.Server || !target.Alive) return;
        if (target.Properties.Attributes?["isMechanical"].AsBool() == true) return;

        float totalBleedDamage = Math.Max(0f, sourceDamage * SerratedBleedDamageFraction);
        if (totalBleedDamage <= 0f) return;

        int armorPiercingTier = damageSource is IArmorPiercing armorPiercing ? armorPiercing.ArmorPiercingTier : 0;
        Bleeds[target.EntityId] = new()
        {
            TicksRemaining = SerratedBleedTicks,
            DamagePerTick = totalBleedDamage / SerratedBleedTicks,
            DamageTier = Math.Max(0, damageSource.DamageTier),
            ArmorPiercingTier = Math.Max(0, armorPiercingTier),
            SourceEntityId = damageSource.SourceEntity?.EntityId ?? damageSource.CauseEntity?.EntityId ?? 0
        };
    }

    private static void TickBleeds(float dt)
    {
        if (_serverApi == null || Bleeds.Count == 0) return;

        List<long>? removed = null;
        foreach ((long entityId, BleedState state) in Bleeds)
        {
            Entity? target = _serverApi.World.GetEntityById(entityId);
            if (target == null || !target.Alive)
            {
                (removed ??= []).Add(entityId);
                continue;
            }

            Entity? sourceEntity = state.SourceEntityId > 0 ? _serverApi.World.GetEntityById(state.SourceEntityId) : null;
            DamageData bleedDamageData = new(EnumDamageType.SlashingAttack, state.DamageTier, state.ArmorPiercingTier);
            DamageSource bleedDamageSource = new TypedDamageSource
            {
                Source = sourceEntity is EntityPlayer ? EnumDamageSource.Player : sourceEntity != null ? EnumDamageSource.Entity : EnumDamageSource.Internal,
                SourceEntity = sourceEntity,
                CauseEntity = sourceEntity,
                DamageTypeData = bleedDamageData,
                IgnoreInvFrames = true,
                DamageTier = bleedDamageData.Tier,
                Type = EnumDamageType.SlashingAttack,
                KnockbackStrength = 0
            };

            target.ReceiveDamage(bleedDamageSource, state.DamagePerTick);
            SpawnBleedParticles(target);
            state.TicksRemaining--;

            if (state.TicksRemaining <= 0)
            {
                (removed ??= []).Add(entityId);
            }
        }

        if (removed == null) return;

        foreach (long entityId in removed)
        {
            Bleeds.Remove(entityId);
        }
    }

    private static void PlayTinkeringSound(EntityAgent byEntity)
    {
        byEntity.World.PlaySoundAt(new AssetLocation("sounds/player/messycraft.ogg"), byEntity.Pos.X, byEntity.Pos.Y, byEntity.Pos.Z, null, true, 32f, 1f);
    }

    private static void SpawnBleedParticles(Entity target)
    {
        float xRadius = Math.Max(0.15f, target.SelectionBox.XSize * 0.35f);
        double centerY = target.Pos.Y + Math.Max(0.25f, target.SelectionBox.YSize * 0.55f);
        Vector3d position = new(target.Pos.X, centerY, target.Pos.Z);
        float intensity = Math.Clamp(xRadius / 0.35f, 0.75f, 2f);

        target.Api.ModLoader.GetModSystem<CombatOverhaulAnimationsSystem>()?.ParticleEffectsManager?.Spawn(BleedParticleEffectCode, position, Vector3.Zero, intensity);
    }

    private static void SetActiveTinkeringAction(ItemStack? stack, TinkeringAction action, long startedMs = 0)
    {
        if (stack == null) return;

        stack.TempAttributes.SetString(ActiveTinkeringActionAttribute, action.Type.ToString());
        stack.TempAttributes.SetString(ActiveTinkeringMaterialAttribute, action.Material);
        stack.TempAttributes.RemoveAttribute(ActiveTinkeringSubmittedAttribute);

        if (startedMs > 0)
        {
            stack.TempAttributes.SetLong(ActiveTinkeringStartedMsAttribute, startedMs);
            stack.TempAttributes.SetLong(ActiveTinkeringLastMsAttribute, startedMs);
            stack.TempAttributes.SetBool(ActiveTinkeringSubmittedAttribute, false);
        }
        else
        {
            stack.TempAttributes.RemoveAttribute(ActiveTinkeringStartedMsAttribute);
            stack.TempAttributes.RemoveAttribute(ActiveTinkeringLastMsAttribute);
        }
    }

    private static bool TryGetActiveTinkeringAction(ItemStack? stack, out TinkeringAction action)
    {
        action = new(TinkeringActionType.None);
        if (stack == null) return false;

        string actionName = stack.TempAttributes.GetString(ActiveTinkeringActionAttribute, "");
        if (!Enum.TryParse(actionName, out TinkeringActionType actionType) || actionType == TinkeringActionType.None) return false;

        string material = stack.TempAttributes.GetString(ActiveTinkeringMaterialAttribute, "");
        action = new(actionType, material);
        return true;
    }

    private static void ClearActiveTinkeringAction(ItemStack? stack)
    {
        if (stack == null) return;

        stack.TempAttributes.RemoveAttribute(ActiveTinkeringActionAttribute);
        stack.TempAttributes.RemoveAttribute(ActiveTinkeringMaterialAttribute);
        stack.TempAttributes.RemoveAttribute(ActiveTinkeringStartedMsAttribute);
        stack.TempAttributes.RemoveAttribute(ActiveTinkeringLastMsAttribute);
        stack.TempAttributes.RemoveAttribute(ActiveTinkeringSubmittedAttribute);
    }

    private static void DamageOffhandTool(EntityAgent byEntity)
    {
        ItemSlot? offhandSlot = byEntity.LeftHandItemSlot;
        if (offhandSlot?.Itemstack == null) return;

        offhandSlot.Itemstack.Collectible.DamageItem(byEntity.World, byEntity, offhandSlot);
        offhandSlot.MarkDirty();
    }

    private static void ConsumeOffhandItem(EntityAgent byEntity)
    {
        ItemSlot? offhandSlot = byEntity.LeftHandItemSlot;
        if (offhandSlot?.Itemstack == null) return;

        offhandSlot.TakeOut(1);
        offhandSlot.MarkDirty();
    }

    private static int GetNailMaxDurability(string material)
    {
        float factor = NailDurabilityFactors.TryGetValue(material, out float configuredFactor) ? configuredFactor : 1.5f;
        return Math.Max(1, (int)MathF.Round(BaseNailDurability * factor));
    }

    private static bool IsSaw(string path)
    {
        return path.Equals("saw", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("saw-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsChisel(string path)
    {
        return path.Equals("chisel", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("chisel-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("truechisel", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("chiselpick", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetNailMaterial(string path, out string material)
    {
        const string prefix = "metalnailsandstrips-";
        material = "";

        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        material = path[prefix.Length..];
        return !string.IsNullOrWhiteSpace(material);
    }

    private static bool WeaponHighestConfiguredDamageTypeIs(ItemStack stack, EnumDamageType damageType)
    {
        return TryGetHighestConfiguredDamage(stack, out EnumDamageType highestDamageType, out float highestDamage)
            && highestDamage > 0f
            && highestDamageType == damageType;
    }

    private static float GetSerratedTooltipBleedDamagePerSecond(ItemStack stack)
    {
        float slashDamage = GetHighestConfiguredDamageForType(stack, EnumDamageType.SlashingAttack);
        float totalBleedDamage = Math.Max(0f, slashDamage * SerratedBleedDamageFraction);
        return totalBleedDamage / SerratedBleedDurationSeconds;
    }

    private static float GetHighestConfiguredDamageForType(ItemStack stack, EnumDamageType damageType)
    {
        ItemStackMeleeWeaponStats stackStats = ItemStackMeleeWeaponStats.FromItemStack(stack);
        float highestDamage = 0f;

        foreach (MeleeAttackStats attack in GetConfiguredOffensiveAttacks(stack))
        {
            foreach (MeleeDamageTypeJson configuredDamageType in attack.DamageTypes ?? Array.Empty<MeleeDamageTypeJson>())
            {
                if (!Enum.TryParse(configuredDamageType.Damage.DamageType, ignoreCase: true, out EnumDamageType parsedDamageType)
                    || parsedDamageType != damageType)
                {
                    continue;
                }

                highestDamage = MathF.Max(highestDamage, GetModifiedConfiguredDamage(configuredDamageType.Damage.Damage, stackStats));
            }
        }

        return highestDamage;
    }

    private static bool TryGetHighestConfiguredDamage(ItemStack stack, out EnumDamageType damageType, out float damage)
    {
        ItemStackMeleeWeaponStats stackStats = ItemStackMeleeWeaponStats.FromItemStack(stack);

        damageType = default;
        damage = 0f;
        bool found = false;

        foreach (MeleeAttackStats attack in GetConfiguredOffensiveAttacks(stack))
        {
            foreach (MeleeDamageTypeJson configuredDamageType in attack.DamageTypes ?? Array.Empty<MeleeDamageTypeJson>())
            {
                if (!Enum.TryParse(configuredDamageType.Damage.DamageType, ignoreCase: true, out EnumDamageType parsedDamageType))
                {
                    continue;
                }

                float modifiedDamage = GetModifiedConfiguredDamage(configuredDamageType.Damage.Damage, stackStats);
                if (!found || modifiedDamage > damage)
                {
                    damageType = parsedDamageType;
                    damage = modifiedDamage;
                    found = true;
                }
            }
        }

        return found;
    }

    private static float GetModifiedConfiguredDamage(float baseDamage, ItemStackMeleeWeaponStats stackStats)
    {
        float damage = baseDamage + stackStats.DamageBonus;
        damage *= stackStats.DamageMultiplier;
        return Math.Max(0f, damage);
    }

    private static IEnumerable<MeleeAttackStats> GetConfiguredOffensiveAttacks(ItemStack stack)
    {
        if (stack.ItemAttributes == null) yield break;

        MeleeWeaponStats? meleeStats = TryReadAttributes<MeleeWeaponStats>(stack);
        if (meleeStats != null)
        {
            foreach (MeleeAttackStats attack in GetConfiguredOffensiveAttacks(meleeStats))
            {
                yield return attack;
            }
        }

        StanceBasedMeleeWeaponStats? stanceStats = TryReadAttributes<StanceBasedMeleeWeaponStats>(stack);
        if (stanceStats != null)
        {
            foreach (MeleeAttackStats attack in GetConfiguredOffensiveAttacks(stanceStats))
            {
                yield return attack;
            }
        }

        MeleeWeaponModeCollectionStats? modeStats = TryReadAttributes<MeleeWeaponModeCollectionStats>(stack);
        if (modeStats != null)
        {
            foreach (MeleeWeaponModeStats mode in modeStats.Modes.Values)
            {
                foreach (MeleeAttackStats attack in GetConfiguredOffensiveAttacks(mode))
                {
                    yield return attack;
                }
            }
        }
    }

    private static T? TryReadAttributes<T>(ItemStack stack) where T : class
    {
        try
        {
            return stack.ItemAttributes?.AsObject<T>();
        }
        catch
        {
            return null;
        }
    }

    private static T? TryReadAttributes<T>(CollectibleObject collectible) where T : class
    {
        try
        {
            return collectible.Attributes?.AsObject<T>();
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<MeleeAttackStats> GetConfiguredOffensiveAttacks(MeleeWeaponStats stats)
    {
        foreach (MeleeAttackStats attack in GetConfiguredOffensiveAttacks(stats.OneHandedStance)) yield return attack;
        foreach (MeleeAttackStats attack in GetConfiguredOffensiveAttacks(stats.TwoHandedStance)) yield return attack;
        foreach (MeleeAttackStats attack in GetConfiguredOffensiveAttacks(stats.OffHandStance)) yield return attack;

        foreach (StanceStats stance in stats.MainHandDualWieldStances?.Values ?? Enumerable.Empty<StanceStats>())
        {
            foreach (MeleeAttackStats attack in GetConfiguredOffensiveAttacks(stance)) yield return attack;
        }

        foreach (StanceStats stance in stats.OffHandDualWieldStances?.Values ?? Enumerable.Empty<StanceStats>())
        {
            foreach (MeleeAttackStats attack in GetConfiguredOffensiveAttacks(stance)) yield return attack;
        }
    }

    private static IEnumerable<MeleeAttackStats> GetConfiguredOffensiveAttacks(StanceStats? stance)
    {
        if (stance == null) yield break;

        if (stance.Attack != null) yield return stance.Attack;
        if (stance.Riposte != null) yield return stance.Riposte;

        foreach (MeleeAttackStats attack in stance.DirectionalAttacks?.Values ?? Enumerable.Empty<MeleeAttackStats>())
        {
            yield return attack;
        }
    }

    private static IEnumerable<MeleeAttackStats> GetConfiguredOffensiveAttacks(StanceBasedMeleeWeaponStats stats)
    {
        foreach (MeleeAttackStats attack in GetConfiguredOffensiveAttacks(stats.OneHanded)) yield return attack;
        foreach (MeleeAttackStats attack in GetConfiguredOffensiveAttacks(stats.TwoHanded)) yield return attack;
        foreach (MeleeAttackStats attack in GetConfiguredOffensiveAttacks(stats.OffHand)) yield return attack;
    }

    private static IEnumerable<MeleeAttackStats> GetConfiguredOffensiveAttacks(StanceBasedMeleeWeaponGripStats? grip)
    {
        if (grip == null) yield break;

        if (grip.DefaultLeftClickAttack != null) yield return grip.DefaultLeftClickAttack;
        if (grip.DefaultRightClickAttack != null) yield return grip.DefaultRightClickAttack;

        foreach (StanceBasedMeleeWeaponAttackStats attack in grip.StanceToStanceLeftClickAttacks?.Values ?? Enumerable.Empty<StanceBasedMeleeWeaponAttackStats>())
        {
            yield return attack;
        }

        foreach (StanceBasedMeleeWeaponAttackStats attack in grip.StanceToStanceRightClickAttacks?.Values ?? Enumerable.Empty<StanceBasedMeleeWeaponAttackStats>())
        {
            yield return attack;
        }
    }

    private static void ApplyAttackSpeedFallback(ItemStack stack, float speedBonus, float multiplier)
    {
        float existingBonus = stack.Attributes.GetFloat(ToolsmithAttackSpeedFallbackAttribute, 0f);
        float existingMultiplier = MathF.Max(0.01f, 1f + existingBonus);
        float baseAttackSpeed = stack.Attributes.GetFloat(AttackSpeedAttribute, 1f) / existingMultiplier;

        stack.Attributes.SetFloat(AttackSpeedAttribute, baseAttackSpeed * multiplier);
        stack.Attributes.SetFloat(ToolsmithAttackSpeedFallbackAttribute, speedBonus);
    }

    private static void RemoveAttackSpeedFallback(ItemStack stack)
    {
        if (!stack.Attributes.HasAttribute(ToolsmithAttackSpeedFallbackAttribute)) return;

        float existingBonus = stack.Attributes.GetFloat(ToolsmithAttackSpeedFallbackAttribute, 0f);
        float existingMultiplier = MathF.Max(0.01f, 1f + existingBonus);
        float baseAttackSpeed = stack.Attributes.GetFloat(AttackSpeedAttribute, 1f) / existingMultiplier;

        stack.Attributes.SetFloat(AttackSpeedAttribute, baseAttackSpeed);
        stack.Attributes.RemoveAttribute(ToolsmithAttackSpeedFallbackAttribute);
    }

    private static bool HasToolsmithSharpness(ItemStack stack)
    {
        return stack.Attributes.HasAttribute(ToolsmithSharpnessCurrentAttribute)
            && stack.Attributes.HasAttribute(ToolsmithSharpnessMaxAttribute);
    }

    private static float GetToolsmithSharpnessCritChance(ItemStack stack)
    {
        if (!HasToolsmithSharpness(stack)) return 0f;

        int maxSharpness = stack.Attributes.GetInt(ToolsmithSharpnessMaxAttribute, 0);
        if (maxSharpness <= 0) return 0f;

        int currentSharpness = stack.Attributes.GetInt(ToolsmithSharpnessCurrentAttribute, 0);
        float sharpnessPercent = Math.Clamp(currentSharpness / (float)maxSharpness, 0f, 1f);

        return sharpnessPercent * ToolsmithMaxSharpnessCritChance;
    }

    private static bool BeforeDisabledTryCraftToolFromSlots(ItemSlot[] slots, ref ItemStack? __result)
    {
        if (IsBlockedToolsmithAssembly(slots))
        {
            __result = null;
            return false;
        }

        return true;
    }

    private static void BeforeToolsmithTryCraftToolFromSlots(ItemSlot[] slots, out bool __state)
    {
        __state = HasToolsmithAssemblyInputs(slots);
        if (__state) _toolsmithWorkbenchAssemblyDepth++;
    }

    private static void AfterToolsmithTryCraftToolFromSlots(bool __state)
    {
        if (__state && _toolsmithWorkbenchAssemblyDepth > 0) _toolsmithWorkbenchAssemblyDepth--;
    }

    private static bool BeforeDisabledAssemblePartBundle(ItemSlot slot)
    {
        return !IsBlockedToolsmithHead(slot.Itemstack);
    }

    private static bool BeforeDisabledAssembleFullTool(ItemSlot bundleSlot, EntityAgent byEntity)
    {
        return !IsBlockedToolsmithBundleAssembly(bundleSlot, byEntity.World);
    }

    private static void AfterCollectibleCreatedByCrafting(ItemSlot outputSlot, IRecipeBase byRecipe)
    {
        ItemStack? stack = outputSlot.Itemstack;
        if (!ShouldKeepBaseWeaponVisual(stack) || stack == null) return;
        if (_toolsmithWeaponCraftingEnabled && IsToolsmithAssemblyRecipe(byRecipe)) return;

        ClearToolsmithAssemblyAttributes(stack);
        outputSlot.MarkDirty();
    }

    private static bool BeforeToolsmithCreatedTool(ItemSlot[] allInputslots, ItemSlot outputSlot, IRecipeBase byRecipe)
    {
        ItemStack? stack = outputSlot.Itemstack;
        if (!ShouldKeepBaseWeaponVisual(stack) || stack == null) return true;
        if (!_toolsmithWeaponCraftingEnabled || !IsToolsmithAssemblyRecipe(byRecipe, allInputslots))
        {
            ClearToolsmithAssemblyAttributes(stack);
            outputSlot.MarkDirty();
            return false;
        }

        stack.Attributes.SetBool(ToolsmithCompatAssembledAttribute, true);
        stack.TempAttributes.SetBool(SuppressToolsmithDurabilityReductionAttribute, true);
        return true;
    }

    private static void AfterToolsmithCreatedTool(ItemSlot[] allInputslots, ItemSlot outputSlot, IRecipeBase byRecipe)
    {
        ItemStack? stack = outputSlot.Itemstack;
        if (!ShouldKeepBaseWeaponVisual(stack) || stack == null) return;
        stack.TempAttributes.RemoveAttribute(SuppressToolsmithDurabilityReductionAttribute);

        if (!_toolsmithWeaponCraftingEnabled || !IsToolsmithAssemblyRecipe(byRecipe, allInputslots))
        {
            ClearToolsmithAssemblyAttributes(stack);
            outputSlot.MarkDirty();
            return;
        }

        stack.Attributes.SetBool(ToolsmithCompatAssembledAttribute, true);
        if (EnsureToolsmithWeaponDurabilityReduced(stack)) outputSlot.MarkDirty();
        ApplyToolsmithCreatedWeapon(stack);
    }

    private static bool IsToolsmithAssemblyRecipe(IRecipeBase byRecipe, ItemSlot[]? allInputslots = null)
    {
        if (IsToolsmithInHandAssembly(byRecipe)) return allInputslots == null || HasToolsmithAssemblyInputs(allInputslots);
        if (!IsToolsmithWorkbenchAssembly(byRecipe)) return false;

        return allInputslots == null || HasToolsmithAssemblyInputs(allInputslots);
    }

    private static bool IsToolsmithInHandAssembly(IRecipeBase byRecipe)
    {
        return byRecipe is GridRecipe recipe
            && recipe.Name?.ToString().Equals("toolsmith:inhandtinkertoolcrafting", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsToolsmithWorkbenchAssembly(IRecipeBase byRecipe)
    {
        return _toolsmithWorkbenchAssemblyDepth > 0
            && byRecipe is GridRecipe recipe
            && recipe.Name == null
            && recipe.Output?.ResolvedItemStack != null;
    }

    private static void ClearToolsmithAssemblyAttributes(ItemStack stack)
    {
        stack.TempAttributes.RemoveAttribute(SuppressToolsmithDurabilityReductionAttribute);
        stack.TempAttributes.RemoveAttribute(ToolsmithMeshRefIdAttribute);

        stack.Attributes.RemoveAttribute(ToolsmithCompatAssembledAttribute);
        stack.Attributes.RemoveAttribute(ToolsmithDurabilityReducedAttribute);
        stack.Attributes.RemoveAttribute(ToolsmithToolHeadAttribute);
        stack.Attributes.RemoveAttribute(ToolsmithToolHandleAttribute);
        stack.Attributes.RemoveAttribute(ToolsmithToolHandleCurrentDurabilityAttribute);
        stack.Attributes.RemoveAttribute(ToolsmithToolHandleMaxDurabilityAttribute);
        stack.Attributes.RemoveAttribute(ToolsmithToolBindingCurrentDurabilityAttribute);
        stack.Attributes.RemoveAttribute(ToolsmithToolBindingMaxDurabilityAttribute);
        stack.Attributes.RemoveAttribute(ToolsmithSpeedBonusAttribute);
        stack.Attributes.RemoveAttribute(ToolsmithSharpnessCurrentAttribute);
        stack.Attributes.RemoveAttribute(ToolsmithSharpnessMaxAttribute);
        stack.Attributes.RemoveAttribute(ModularMultiPartRenderDataAttribute);
        stack.Attributes.RemoveAttribute(ModularPartRenderDataAttribute);
        stack.Attributes.RemoveAttribute(PartShapeIndexAttribute);
        stack.Attributes.RemoveAttribute(ToolsmithMeshRefIdAttribute);

        WeaponBuffSystem.Current?.RemoveBuff(stack, ToolsmithAttackSpeedBuffCode, ToolsmithAttackSpeedBuffSource);
        RemoveAttackSpeedFallback(stack);
    }

    private static bool BeforeToolsmithHeldItemInfo(ItemSlot inSlot)
    {
        ItemStack? stack = inSlot.Itemstack;
        if (EnsureToolsmithWeaponDurabilityReduced(stack)) inSlot.MarkDirty();
        return !ShouldKeepBaseWeaponVisual(stack) || IsToolsmithCompatAssembledWeapon(stack);
    }

    private static bool BeforeToolsmithBindingHeldItemInfo(ItemSlot inSlot)
    {
        ItemStack? stack = inSlot?.Itemstack;
        if (stack?.Collectible?.Code == null) return false;

        return ToolsmithBindingTooltipStateReady();
    }

    private static bool ToolsmithBindingTooltipStateReady()
    {
        Type? modSystemType = AccessTools.TypeByName("Toolsmith.ToolsmithModSystem");
        object? stats = modSystemType?.GetField("Stats", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        if (stats == null) return false;

        Type statsType = stats.GetType();
        return statsType.GetField("BindingParts", BindingFlags.Public | BindingFlags.Instance)?.GetValue(stats) != null
            && statsType.GetField("BindingStats", BindingFlags.Public | BindingFlags.Instance)?.GetValue(stats) != null;
    }

    private static bool BeforeToolsmithDamageItem(ItemSlot itemslot)
    {
        ItemStack? stack = itemslot.Itemstack;
        if (EnsureToolsmithWeaponDurabilityReduced(stack)) itemslot.MarkDirty();
        return !ShouldKeepBaseWeaponVisual(stack) || IsToolsmithCompatAssembledWeapon(stack);
    }

    private static bool BeforeToolsmithStackBehavior(ItemStack itemstack)
    {
        EnsureToolsmithWeaponDurabilityReduced(itemstack);
        return !ShouldKeepBaseWeaponVisual(itemstack) || IsToolsmithCompatAssembledWeapon(itemstack);
    }

    private static bool IsToolsmithCompatAssembledWeapon(ItemStack? stack)
    {
        return HasToolsmithCompatAssembledMarker(stack) && ShouldKeepBaseWeaponVisual(stack);
    }

    private static bool HasToolsmithCompatAssembledMarker(ItemStack? stack)
    {
        return stack != null
            && (stack.Attributes.GetBool(ToolsmithCompatAssembledAttribute, false)
                || stack.Attributes.HasAttribute(ToolsmithAttackSpeedFallbackAttribute));
    }

    private static bool HasToolsmithAssemblyInputs(ItemSlot[] allInputslots)
    {
        bool hasToolHead = false;
        bool hasToolHandle = false;

        foreach (ItemSlot slot in allInputslots)
        {
            ItemStack? stack = slot.Itemstack;
            if (stack == null) continue;

            if (HasCollectibleBehaviorNamed(stack, ToolsmithTinkeredToolBehaviorName) && HasStoredToolsmithParts(stack)) return true;

            hasToolHead |= HasCollectibleBehaviorNamed(stack, ToolsmithToolHeadBehaviorName);
            hasToolHandle |= HasCollectibleBehaviorNamed(stack, ToolsmithToolHandleBehaviorName);
        }

        return hasToolHead && hasToolHandle;
    }

    private static bool IsBlockedToolsmithAssembly(ItemSlot[]? slots)
    {
        if (slots == null) return false;

        foreach (ItemSlot slot in slots)
        {
            ItemStack? stack = slot.Itemstack;
            if (stack == null || !HasCollectibleBehaviorNamed(stack, ToolsmithToolHeadBehaviorName)) continue;

            if (IsBlockedToolsmithHead(stack)) return true;
        }

        return false;
    }

    private static bool IsBlockedToolsmithHead(ItemStack? headStack)
    {
        return IsKnownToolsmithHeadCode(headStack?.Collectible?.Code)
            || ShouldKeepBaseWeaponVisual(GetToolsmithOutputForHead(headStack));
    }

    private static bool IsBlockedToolsmithBundleAssembly(ItemSlot? bundleSlot, IWorldAccessor world)
    {
        ItemStack? bundleStack = bundleSlot?.Itemstack;
        if (bundleStack == null || !HasStoredToolsmithParts(bundleStack)) return false;

        ItemStack? headStack = bundleStack.Attributes.GetItemstack(ToolsmithToolHeadAttribute, null);
        headStack?.ResolveBlockOrItem(world);

        return IsKnownToolsmithHeadCode(headStack?.Collectible?.Code)
            || ShouldKeepBaseWeaponVisual(GetToolsmithOutputForHead(headStack));
    }

    private static CollectibleObject? GetToolsmithOutputForHead(ItemStack? headStack)
    {
        AssetLocation? code = headStack?.Collectible?.Code;
        if (code == null) return null;

        Type? recipeRegister = AccessTools.TypeByName("Toolsmith.RecipeRegisterModSystem");
        FieldInfo? recipesField = recipeRegister == null ? null : AccessTools.Field(recipeRegister, "TinkerToolGridRecipes");
        if (recipesField?.GetValue(null) is not IDictionary recipes) return null;

        string key = code.ToString();
        return recipes.Contains(key) ? recipes[key] as CollectibleObject : null;
    }

    private static bool HasStoredToolsmithParts(ItemStack stack)
    {
        return stack.Attributes.HasAttribute(ToolsmithToolHeadAttribute)
            && stack.Attributes.HasAttribute(ToolsmithToolHandleAttribute);
    }

    private static bool HasCollectibleBehaviorNamed(ItemStack stack, string behaviorName)
    {
        return stack.Collectible?.CollectibleBehaviors?
            .Any(behavior => behavior.GetType().Name == behaviorName) == true;
    }

    private static void PatchToolsmithBehaviorMethod(Type behaviorType, string methodName, string prefixName)
    {
        MethodInfo? method = AccessTools.Method(behaviorType, methodName);
        if (method == null) return;

        _harmony?.Patch(method, prefix: new HarmonyMethod(typeof(ToolsmithCompat), prefixName));
    }

    private static void PatchCollectibleCreatedByCraftingCleanup()
    {
        MethodInfo? method = AccessTools.Method(
            typeof(CollectibleObject),
            nameof(CollectibleObject.OnCreatedByCrafting),
            [typeof(ItemSlot[]), typeof(ItemSlot), typeof(IRecipeBase)]);
        if (method == null) return;

        _harmony?.Patch(method, postfix: new HarmonyMethod(typeof(ToolsmithCompat), nameof(AfterCollectibleCreatedByCrafting)));
    }

    private static void PatchCollectibleObjectMethod(string methodName, Type[] parameters, string prefixName)
    {
        MethodInfo? method = AccessTools.Method(typeof(CollectibleObject), methodName, parameters);
        if (method == null) return;

        _harmony?.Patch(method, prefix: new HarmonyMethod(typeof(ToolsmithCompat), prefixName));
    }

    private static string FormatSignedPercent(float value)
    {
        string sign = value >= 0f ? "+" : "";
        return sign + (value * 100f).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }

    private static string FormatDamage(float value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatDuration(float seconds)
    {
        return seconds.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private static void AppendTooltipLineOnce(StringBuilder description, string line)
    {
        if (description.ToString().Contains(line, StringComparison.Ordinal)) return;

        description.AppendLine(line);
    }

    private static void RemoveLegacyAttackSpeedBuffTooltip(StringBuilder description)
    {
        description.Replace(LegacyAttackSpeedTooltipName + "\r\n", "");
        description.Replace(LegacyAttackSpeedTooltipName + "\n", "");
        description.Replace(LegacyAttackSpeedTooltipDescription + "\r\n", "");
        description.Replace(LegacyAttackSpeedTooltipDescription + "\n", "");
    }

    private static bool BeforeToolsmithMultipartRender(ItemStack itemstack)
    {
        return !ShouldKeepBaseWeaponVisual(itemstack);
    }

    private static bool ShouldKeepBaseWeaponVisual(ItemStack? stack)
    {
        return ShouldKeepBaseWeaponVisual(stack?.Collectible);
    }

    private static bool ShouldKeepBaseWeaponVisual(CollectibleObject? collectible)
    {
        if (collectible == null) return false;

        return KeepBaseWeaponVisualCache.GetOrAdd(collectible, ComputeShouldKeepBaseWeaponVisual);
    }

    private static bool ComputeShouldKeepBaseWeaponVisual(CollectibleObject collectible)
    {
        if (collectible is IHasMeleeWeaponActions) return true;

        return HasCombatOverhaulMeleeStats(collectible);
    }

    private static bool HasCombatOverhaulMeleeStats(CollectibleObject collectible)
    {
        if (collectible.Attributes == null) return false;

        MeleeWeaponStats? meleeStats = TryReadAttributes<MeleeWeaponStats>(collectible);
        if (meleeStats != null && HasConfiguredOffensiveAttacks(meleeStats)) return true;

        StanceBasedMeleeWeaponStats? stanceStats = TryReadAttributes<StanceBasedMeleeWeaponStats>(collectible);
        if (stanceStats != null && HasConfiguredOffensiveAttacks(stanceStats)) return true;

        MeleeWeaponModeCollectionStats? modeStats = TryReadAttributes<MeleeWeaponModeCollectionStats>(collectible);
        if (modeStats?.Modes != null)
        {
            foreach (MeleeWeaponModeStats mode in modeStats.Modes.Values)
            {
                if (HasConfiguredOffensiveAttacks(mode)) return true;
            }
        }

        return false;
    }

    private static bool HasConfiguredOffensiveAttacks(MeleeWeaponStats stats)
    {
        foreach (MeleeAttackStats _ in GetConfiguredOffensiveAttacks(stats))
        {
            return true;
        }

        return false;
    }

    private static bool HasConfiguredOffensiveAttacks(StanceBasedMeleeWeaponStats stats)
    {
        foreach (MeleeAttackStats _ in GetConfiguredOffensiveAttacks(stats))
        {
            return true;
        }

        return false;
    }
}
