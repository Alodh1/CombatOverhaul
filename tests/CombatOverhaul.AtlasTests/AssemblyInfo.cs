using Atlas.XUnit;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: AtlasMods(
    "mods/overhaulliblegacycompat",
    "mods/jsonpatcheslib.zip",
    "mods/vsimgui.zip",
    "mods/toolsmith.zip",
    "mods/combatoverhaulfork")]
