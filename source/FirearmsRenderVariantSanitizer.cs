using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Firearms;

internal static class FirearmsRenderVariantSanitizer
{
    private const string HarmonyId = "firearmsfork.render-variant-sanitizer";

    private static Harmony? _harmony;

    public static void Start(ICoreClientAPI api)
    {
        Type? rendererType = AccessTools.TypeByName("Vintagestory.Client.NoObf.InventoryItemRenderer");
        Type? clientMainType = AccessTools.TypeByName("Vintagestory.Client.NoObf.ClientMain");
        if (rendererType == null || clientMainType == null)
        {
            api.Logger.Warning("[maltiezfirearms] Could not find vanilla item renderer types. Invalid renderVariant stacks may still crash in GUI rendering.");
            return;
        }

        System.Reflection.MethodInfo? method = AccessTools.Method(
            rendererType,
            "GetItemStackRenderInfo",
            [clientMainType, typeof(ItemSlot), typeof(EnumItemRenderTarget), typeof(float)]);
        if (method == null)
        {
            api.Logger.Warning("[maltiezfirearms] Could not patch item render info creation. Invalid renderVariant stacks may still crash in GUI rendering.");
            return;
        }

        _harmony ??= new Harmony(HarmonyId);
        _harmony.Patch(method, prefix: new HarmonyMethod(typeof(FirearmsRenderVariantSanitizer), nameof(SanitizeRenderVariantPrefix)));
    }

    public static void Stop()
    {
        _harmony?.UnpatchAll(HarmonyId);
        _harmony = null;
    }

    private static void SanitizeRenderVariantPrefix(ItemSlot inSlot)
    {
        FirearmRenderVariantUtil.SanitizeRenderVariant(inSlot);
    }
}
