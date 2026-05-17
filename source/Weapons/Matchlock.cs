using CombatOverhaul.Animations;
using CombatOverhaul.Inputs;
using CombatOverhaul.RangedSystems;
using CombatOverhaul.Utils;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Firearms;


public class MatchlockClient : MuzzleloaderClient
{
    public MatchlockClient(ICoreClientAPI api, Item item) : base(api, item)
    {
    }

    protected override bool ShootCallback(ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        SetState(MuzzleloaderState.Aim);

        return true;
    }
}

public class MatchlockItem : Item, IHasWeaponLogic, IHasRangedWeaponLogic, IHasIdleAnimations
{
    public MatchlockClient? ClientLogic { get; private set; }
    public MuzzleloaderServer? ServerLogic { get; private set; }

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

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        if (Stats != null && Stats.ProficiencyStat != "")
        {
            string description = Lang.Get("combatoverhaul:iteminfo-proficiency", Lang.Get($"combatoverhaul:proficiency-{Stats.ProficiencyStat}"));
            dsc.AppendLine(description);
        }

        if (Stats != null)
        {
            dsc.AppendLine(Lang.Get("combatoverhaul:iteminfo-range-weapon-damage", Stats.BulletDamageMultiplier, Stats.BulletDamageStrength));
            dsc.AppendLine("");
        }
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
    }

    public override int GetRemainingDurability(ItemStack itemstack)
    {
        int durability = base.GetRemainingDurability(itemstack);
        int maxDurability = GetMaxDurability(itemstack);
        if (durability > maxDurability)
        {
            itemstack.Attributes.RemoveAttribute("durability");
            return maxDurability;
        }
        return durability;
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
