using CombatOverhaul.Animations;
using CombatOverhaul.Implementations;
using CombatOverhaul.Inputs;
using CombatOverhaul.MeleeSystems;
using CombatOverhaul.RangedSystems;
using CombatOverhaul.Utils;
using OpenTK.Mathematics;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Firearms;

public enum MusketState
{
    Unloaded,
    Loading,
    Loaded,
    Priming,
    Primed,
    Cocking,
    Cocked,
    Aim,
    Shoot,
    AttackWindup,
    Attack,
    Cooldown
}

public enum MusketLoadingStage
{
    Unloaded,
    Loading,
    Priming,
    AttachBayonet,
    DamageBayonet,
    DetachBayonet
}

internal static class FirearmRenderVariantUtil
{
    private const string BayonetInventoryId = "bayonet";
    private static readonly string[] BayonetVariantSuffixes = ["plug-plain", "plug-decorated", "socket-plain", "socket-decorated", "plain", "decorated"];

    public static bool TryGetBayonetRenderVariant(ItemStack firearm, ItemStack bayonet, out int renderVariant)
    {
        renderVariant = GetBayonetRenderVariantCandidate(bayonet);
        if (IsValidRenderVariant(firearm, renderVariant)) return true;

        if (TryResolveBayonetRenderVariantFromAlternates(firearm, bayonet, out renderVariant)) return true;

        renderVariant = 0;
        return false;
    }

    public static bool SanitizeRenderVariant(ItemStack? stack)
    {
        if (stack?.Collectible == null || !stack.Attributes.HasAttribute("renderVariant")) return false;

        int renderVariant = stack.Attributes.GetInt("renderVariant", 0);
        if (IsValidRenderVariant(stack, renderVariant)) return false;

        stack.Attributes.RemoveAttribute("renderVariant");
        return true;
    }

    public static bool SanitizeRenderVariant(ItemSlot? slot)
    {
        ItemStack? stack = slot?.Itemstack;
        if (stack?.Collectible == null || !stack.Attributes.HasAttribute("renderVariant")) return false;

        int renderVariant = stack.Attributes.GetInt("renderVariant", 0);
        if (IsValidRenderVariant(stack, renderVariant)) return false;

        if (stack.Item is MusketItem && slot != null)
        {
            ItemInventoryBuffer bayonetInventory = new();
            bayonetInventory.Read(slot, BayonetInventoryId);
            try
            {
                if (bayonetInventory.Items.Count > 0 && TryGetBayonetRenderVariant(stack, bayonetInventory.Items[0], out int repairedRenderVariant))
                {
                    stack.Attributes.SetInt("renderVariant", repairedRenderVariant);
                    return true;
                }
            }
            finally
            {
                bayonetInventory.Clear();
            }
        }

        stack.Attributes.RemoveAttribute("renderVariant");
        return true;
    }

    public static bool IsValidRenderVariant(ItemStack stack, int renderVariant)
    {
        if (renderVariant < 2) return true;

        int alternateIndex = renderVariant - 2;
        return alternateIndex >= 0 && (stack.Item?.Shape?.Alternates?.Length ?? 0) > alternateIndex;
    }

    private static int GetBayonetRenderVariantCandidate(ItemStack bayonet)
    {
        string path = bayonet.Collectible?.Code?.Path ?? bayonet.Item?.Code?.Path ?? "";
        string code = bayonet.Collectible?.Code?.ToString() ?? bayonet.Item?.Code?.ToString() ?? path;

        Dictionary<string, int>? renderVariantByType = bayonet.Collectible?.Attributes?["musketRenderVariantByType"].AsObject<Dictionary<string, int>>();
        if (renderVariantByType != null)
        {
            foreach ((string wildcard, int renderVariant) in renderVariantByType)
            {
                if (WildcardUtil.Match(wildcard, path) || WildcardUtil.Match(wildcard, code))
                {
                    return Math.Max(0, renderVariant);
                }
            }
        }

        if (WildcardUtil.Match("*-plug-plain", path)) return 2;
        if (WildcardUtil.Match("*-plug-decorated", path)) return 3;
        if (WildcardUtil.Match("*-socket-plain", path)) return 4;
        if (WildcardUtil.Match("*-socket-decorated", path)) return 5;

        return 0;
    }

    private static bool TryResolveBayonetRenderVariantFromAlternates(ItemStack firearm, ItemStack bayonet, out int renderVariant)
    {
        renderVariant = 0;

        string bayonetPath = bayonet.Collectible?.Code?.Path ?? bayonet.Item?.Code?.Path ?? "";
        string suffix = BayonetVariantSuffixes.FirstOrDefault(element => bayonetPath.Contains(element, StringComparison.OrdinalIgnoreCase)) ?? "";
        if (suffix == "") return false;

        CompositeShape[]? alternates = firearm.Item?.Shape?.Alternates;
        if (alternates == null || alternates.Length == 0) return false;

        for (int index = 0; index < alternates.Length; index++)
        {
            string alternatePath = alternates[index].Base?.Path ?? alternates[index].Base?.ToString() ?? "";
            if (alternatePath.Contains(suffix, StringComparison.OrdinalIgnoreCase))
            {
                renderVariant = index + 2;
                return true;
            }
        }

        return false;
    }
}

public class MusketStats
{
    public string BayonetWildcard { get; set; } = "*bayonet-*";

    public MeleeAttackStats BayonetAttack { get; set; } = new();
    public string BayonetAttackAnimation { get; set; } = "";
    public string BayonetAttackTpAnimation { get; set; } = "";
    public float AnimationStaggerOnHitDurationMs { get; set; } = 100;
}

public class MusketClient : MuzzleloaderClient, IOnGameTick
{
    public MusketClient(ICoreClientAPI api, Item item) : base(api, item)
    {
        StatsMusket = item.Attributes.AsObject<MusketStats>();

        BayonetAttack = new(api, StatsMusket.BayonetAttack);
        RegisterCollider(item.Code.ToString(), "bayonet-", BayonetAttack);
    }

    public virtual void RenderDebugCollider(ItemSlot inSlot, IClientPlayer byPlayer)
    {
        foreach (MeleeDamageType damageType in BayonetAttack.DamageTypes)
        {
            damageType.RelativeCollider.Transform(byPlayer.Entity.Pos, byPlayer.Entity, inSlot, Api, right: true)?.Render(Api, byPlayer.Entity);
        }
    }

    public void OnGameTick(ItemSlot slot, EntityPlayer player, ref int state, bool mainHand)
    {
        switch (GetState<MusketState>(mainHand))
        {
            case MusketState.Attack:
                {
                    TryAttack(BayonetAttack, slot, player, mainHand);
                }
                break;
            default:
                break;
        }
    }

    protected readonly MusketStats StatsMusket;
    protected readonly ItemInventoryBuffer BayonetInventory = new();
    protected const string BayonetInventoryId = "bayonet";
    protected MeleeAttack BayonetAttack;


    [HotkeyEventHandler("attach-bayonet", "maltiezfirearms:attach-bayonet", GlKeys.B)]
    protected virtual bool Bayonet(ItemSlot slot, EntityPlayer player, ref int state, KeyCombination keyCombination, bool mainHand, AttackDirection direction)
    {
        if (slot.Itemstack?.Item is not MusketItem) return false;

        BayonetInventory.Read(slot, BayonetInventoryId);
        if (BayonetInventory.Items.Count == 0)
        {
            if (!WildcardUtil.Match(StatsMusket.BayonetWildcard, player.LeftHandItemSlot.Itemstack?.Item?.Code?.ToString() ?? "")) return false;

            RangedWeaponSystem.Reload(slot, player.LeftHandItemSlot, 1, mainHand, ServerAttachBayonetCallback, data: SerializeLoadingStage(MusketLoadingStage.AttachBayonet));

            BayonetInventory.Clear();
        }
        else
        {
            RangedWeaponSystem.Reload(slot, player.LeftHandItemSlot, 1, mainHand, ServerAttachBayonetCallback, data: SerializeLoadingStage(MusketLoadingStage.DetachBayonet));
            BayonetInventory.Clear();
        }

        return true;
    }
    protected virtual void ServerAttachBayonetCallback(bool result)
    {

    }

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Active)]
    protected virtual bool Attack(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (InteractionsTester.PlayerTriesToInteract(player, mainHand, eventData)) return false;
        if (CheckState(state, MusketState.Loading, MusketState.Aim, MusketState.Priming, MusketState.Shoot)) return false;
        if (CheckState(state, MusketState.AttackWindup, MusketState.Attack, MusketState.Cooldown)) return false;

        BayonetInventory.Read(slot, BayonetInventoryId);
        if (!BayonetInventory.Items.Any())
        {
            BayonetInventory.Clear();
            return false;
        }
        BayonetInventory.Clear();

        SetState(MusketState.AttackWindup, mainHand);
        BayonetAttack.Start(player.Player);
        AnimationBehavior?.Play(
            mainHand,
            StatsMusket.BayonetAttackAnimation,
            animationSpeed: GetAnimationSpeed(player, "spearsProficiency"),
            category: AnimationCategory(mainHand),
            callback: () => AttackAnimationCallback(slot, player, mainHand),
            callbackHandler: code => AttackAnimationCallbackHandler(slot, player, code, mainHand));
        TpAnimationBehavior?.Play(
            mainHand,
            StatsMusket.BayonetAttackAnimation,
            animationSpeed: GetAnimationSpeed(player, "spearsProficiency"),
            category: AnimationCategory(mainHand));
        AnimationBehavior?.StopAllVanillaAnimations(mainHand);
        if (TpAnimationBehavior == null) AnimationBehavior?.PlayVanillaAnimation(StatsMusket.BayonetAttackTpAnimation, mainHand);

        return true;
    }
    protected virtual void TryAttack(MeleeAttack attack, ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        ItemStackMeleeWeaponStats stackStats;
        bool hasBayonet = false;
        BayonetInventory.Read(slot, BayonetInventoryId);
        if (!BayonetInventory.Items.Any())
        {
            stackStats = new();
        }
        else
        {
            ItemStack bayonetStack = BayonetInventory.Items[0];
            stackStats = ItemStackMeleeWeaponStats.FromItemStack(bayonetStack);
            hasBayonet = true;
        }
        BayonetInventory.Clear();

        bool attacked = attack.Attack(
            player.Player,
            slot,
            mainHand,
            out IEnumerable<(Block block, Vector3d point)> terrainCollision,
            out IEnumerable<(Vintagestory.API.Common.Entities.Entity entity, Vector3d point)> entitiesCollision,
            stackStats,
            AttackDirection.Top);

        if (hasBayonet && attacked && StatsMusket.AnimationStaggerOnHitDurationMs > 0)
        {
            AnimationBehavior?.SetSpeedModifier(AttackImpactFunction);
            RangedWeaponSystem.Reload(slot, player.LeftHandItemSlot, 1, mainHand, ServerAttachBayonetCallback, data: SerializeLoadingStage(MusketLoadingStage.DamageBayonet));
        }
    }
    protected virtual bool AttackImpactFunction(TimeSpan duration, ref TimeSpan delta)
    {
        TimeSpan totalDuration = TimeSpan.FromMilliseconds(StatsMusket.AnimationStaggerOnHitDurationMs);

        delta = TimeSpan.Zero;

        return duration < totalDuration;
    }
    protected virtual bool AttackAnimationCallback(ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        AnimationBehavior?.PlayReadyAnimation(mainHand);
        TpAnimationBehavior?.PlayReadyAnimation(mainHand);

        int state = PlayerBehavior?.GetState(mainHand) ?? 0;
        OnSelected(slot, player, mainHand, ref state);
        PlayerBehavior?.SetState(state, mainHand);

        return true;
    }
    protected virtual void AttackAnimationCallbackHandler(ItemSlot slot, EntityPlayer player, string callbackCode, bool mainHand)
    {
        switch (callbackCode)
        {
            case "start":
                SetState(MusketState.Attack, mainHand);
                break;
            case "stop":
                SetState(MusketState.Cooldown, mainHand);
                break;
            case "ready":
                int state = PlayerBehavior?.GetState(mainHand) ?? 0;
                OnSelected(slot, player, mainHand, ref state);
                PlayerBehavior?.SetState(state, mainHand);
                break;
        }
    }
    protected static string AnimationCategory(bool mainHand = true) => mainHand ? "main" : "mainOffhand";
    protected static void RegisterCollider(string item, string type, MeleeAttack attack)
    {
#if DEBUG
        int typeIndex = 0;
        foreach (MeleeDamageType damageType in attack.DamageTypes)
        {
            AnimationsManager.RegisterCollider(item, type + typeIndex++, damageType);
        }
#endif
    }
}

public class MusketServer : MuzzleloaderServer
{
    public MusketServer(ICoreServerAPI api, Item item) : base(api, item)
    {
    }

    public override bool Reload(IServerPlayer player, ItemSlot slot, ItemSlot? ammoSlot, ReloadPacket packet)
    {
        MusketLoadingStage currentStage = GetLoadingStage<MusketLoadingStage>(packet);

        switch (currentStage)
        {
            case MusketLoadingStage.AttachBayonet:
                {
                    if (ammoSlot?.Itemstack == null) return false;

                    ItemStack bayonet = ammoSlot.TakeOut(1);
                    if (bayonet == null || bayonet.StackSize <= 0) return false;
                    bayonet.ResolveBlockOrItem(Api.World);

                    BayonetInventory.Read(slot, BayonetInventoryId);
                    if (BayonetInventory.Items.Count > 0)
                    {
                        BayonetInventory.Clear();
                        return false;
                    }
                    BayonetInventory.Items.Add(bayonet);
                    BayonetInventory.Write(slot);
                    BayonetInventory.Clear();

                    if (slot.Itemstack != null && FirearmRenderVariantUtil.TryGetBayonetRenderVariant(slot.Itemstack, bayonet, out int renderVariant))
                    {
                        slot.Itemstack.Attributes.SetInt("renderVariant", renderVariant);
                    }
                    else
                    {
                        slot.Itemstack?.Attributes?.RemoveAttribute("renderVariant");
                        Api.Logger.Warning($"[maltiezfirearms] Could not resolve a valid renderVariant for bayonet '{bayonet.Collectible?.Code}' on firearm '{slot.Itemstack?.Collectible?.Code}'. The attachment was kept but the alternate firearm model was not applied.");
                    }
                    slot.MarkDirty();
                    ammoSlot.MarkDirty();
                }
                return true;
            case MusketLoadingStage.DamageBayonet:
                {
                    BayonetInventory.Read(slot, BayonetInventoryId);
                    if (BayonetInventory.Items.Count == 0)
                    {
                        BayonetInventory.Clear();
                        return false;
                    }

                    ItemStack bayonet = BayonetInventory.Items[0];
                    bayonet.ResolveBlockOrItem(Api.World);
                    DummySlot dummySlot = new(bayonet);
                    bayonet.Item.DamageItem(Api.World, player.Entity, dummySlot, 1);
                    if (dummySlot.Empty)
                    {
                        BayonetInventory.Items.Clear();
                    }

                    BayonetInventory.Write(slot);
                    BayonetInventory.Clear();
                }
                return true;
            case MusketLoadingStage.DetachBayonet:
                {
                    BayonetInventory.Read(slot, BayonetInventoryId);
                    if (BayonetInventory.Items.Count == 0)
                    {
                        BayonetInventory.Clear();
                        slot.Itemstack?.Attributes?.RemoveAttribute("renderVariant");
                        slot.MarkDirty();
                        return false;
                    }

                    ItemStack bayonet = BayonetInventory.Items[0];
                    bayonet.ResolveBlockOrItem(Api.World);
                    BayonetInventory.Items.Clear();
                    BayonetInventory.Write(slot);
                    BayonetInventory.Clear();

                    if (!player.Entity.TryGiveItemStack(bayonet))
                    {
                        Api.World.SpawnItemEntity(bayonet, player.Entity.Pos.AsBlockPos);
                    }

                    slot.Itemstack?.Attributes?.RemoveAttribute("renderVariant");
                    slot.MarkDirty();
                }
                return true;
            default:
                return base.Reload(player, slot, ammoSlot, packet);
        }
    }

    protected readonly ItemInventoryBuffer BayonetInventory = new();
    protected const string BayonetInventoryId = "bayonet";
}

public class MusketItem : Item, IHasWeaponLogic, IHasRangedWeaponLogic, IHasIdleAnimations, IOnGameTick
{
    public MusketClient? ClientLogic { get; private set; }
    public MusketServer? ServerLogic { get; private set; }

    public AnimationRequestByCode IdleAnimation { get; private set; }
    public AnimationRequestByCode ReadyAnimation { get; private set; }

    public MuzzleloaderStats? Stats { get; private set; }

    IClientWeaponLogic? IHasWeaponLogic.ClientLogic
    {
        get
        {
            if (ClientLogic == null && _clientApi != null)
            {
                TryInitClientLogic(_clientApi);
            }
            return ClientLogic;
        }
    }
    IServerRangedWeaponLogic? IHasRangedWeaponLogic.ServerWeaponLogic
    {
        get
        {
            if (ServerLogic == null && _serverApi != null)
            {
                TryInitServerLogic(_serverApi);
            }
            return ServerLogic;
        }
    }

    public void OnGameTick(ItemSlot slot, EntityPlayer player, ref int state, bool mainHand) => ClientLogic?.OnGameTick(slot, player, ref state, mainHand);

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        if (Stats != null)
        {
            dsc.AppendLine(Lang.Get("combatoverhaul:iteminfo-range-weapon-damage", Stats.BulletDamageMultiplier, Stats.BulletDamageStrength));
            dsc.AppendLine("");
        }
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api is ICoreClientAPI clientAPI)
        {
            Stats = Attributes.AsObject<MuzzleloaderStats>();
            IdleAnimation = new(Stats.IdleAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            ReadyAnimation = new(Stats.ReadyAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            _clientApi = clientAPI;
        }

        if (api is ICoreServerAPI serverAPI)
        {
            _serverApi = serverAPI;
            TryInitServerLogic(serverAPI);
        }
    }

    public override void OnHeldRenderOpaque(ItemSlot inSlot, IClientPlayer byPlayer)
    {
        FirearmRenderVariantUtil.SanitizeRenderVariant(inSlot.Itemstack);
        base.OnHeldRenderOpaque(inSlot, byPlayer);

        if (DebugWindowManager.RenderDebugColliders)
        {
            ClientLogic?.RenderDebugCollider(inSlot, byPlayer);
        }
    }

    public override int GetRemainingDurability(ItemStack itemstack)
    {
        FirearmRenderVariantUtil.SanitizeRenderVariant(itemstack);
        int durability = base.GetRemainingDurability(itemstack);
        int maxDurability = GetMaxDurability(itemstack);
        if (durability > maxDurability)
        {
            itemstack.Attributes.RemoveAttribute("durability");
            return maxDurability;
        }
        return durability;
    }

    public override void OnCreatedByCrafting(ItemSlot[] allInputslots, ItemSlot outputSlot, IRecipeBase byRecipe)
    {
        base.OnCreatedByCrafting(allInputslots, outputSlot, byRecipe);

        GeneralUtils.MarkItemStack(outputSlot);
        outputSlot.MarkDirty();
    }

    private ICoreClientAPI? _clientApi;
    private ICoreServerAPI? _serverApi;

    private void TryInitClientLogic(ICoreClientAPI clientAPI)
    {
        try
        {
            ClientLogic = new(clientAPI, this);
        }
        catch (Exception exception)
        {
            clientAPI.Logger.Warning($"[maltiezfirearms] Delayed client logic init for {Code}: {exception.Message}");
        }
    }

    private void TryInitServerLogic(ICoreServerAPI serverAPI)
    {
        try
        {
            ServerLogic = new(serverAPI, this);
        }
        catch (Exception exception)
        {
            serverAPI.Logger.Warning($"[maltiezfirearms] Delayed server logic init for {Code}: {exception.Message}");
        }
    }
}
