using CombatOverhaul;
using CombatOverhaul.Animations;
using CombatOverhaul.Armor;
using CombatOverhaul.Implementations;
using CombatOverhaul.Inputs;
using CombatOverhaul.RangedSystems;
using CombatOverhaul.RangedSystems.Aiming;
using CombatOverhaul.Utils;
using OpenTK.Mathematics;
using System.Diagnostics;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Firearms;

public enum RevolverState
{
    Idle,
    StartLoading,
    Loading,
    FinishLoading,
    Cocking,
    Aim,
    Shoot
}

public enum RevolverReadyState
{
    Unloaded,
    Ready,
    Fired
}

public class RevolverLoadStageStats
{
    public string LoadedAnimation { get; set; } = "";
    public string UnloadedAnimation { get; set; } = "";
    public string LoadingAnimation { get; set; } = "";
    public string CockingAnimation { get; set; } = "";
    public string CockedAnimation { get; set; } = "";
    public string ShootAnimation { get; set; } = "";
    public string FiredAnimation { get; set; } = "";
    public string AimAnimation { get; set; } = "";
    public string StartLoadingAnimation { get; set; } = "";
    public string ContinueLoadingAnimation { get; set; } = "";
    public string FinishLoadingAnimation { get; set; } = "";
    public string CancelLoadingAnimation { get; set; } = "";
    public string CylinderPositionAnimation { get; set; } = "";

    public int LoadToStage { get; set; } = 0;
    public int UnloadToStage { get; set; } = 0;

    public bool CanFire { get; set; } = true;
    public bool CanLoad { get; set; } = true;

    public int WaddingUsed { get; set; } = 1;
    public int PowderConsumption { get; set; } = 1;
    public int BulletsFired { get; set; } = 1;
    public int BulletLoaded { get; set; } = 1;

    public float LoadSpeedPenalty { get; set; } = -2.0f;
}

public class RevolverFiringStats
{
    public float[] DispersionMOA { get; set; } = [0, 0];
    public float BulletDamageMultiplier { get; set; } = 1;
    public int BulletDamageTier { get; set; } = 1;
    public float BulletVelocity { get; set; } = 1;
    public float Zeroing { get; set; } = 0;
}

public class RevolverStats : WeaponStats
{
    public bool AutomaticFire { get; set; } = false;

    public RevolverLoadStageStats? LoadStagesTemplate { get; set; } = new();
    public RevolverLoadStageStats[] LoadStages { get; set; } = [];
    public RevolverFiringStats FiringStats { get; set; } = new();
    public AimingStatsJson Aiming { get; set; } = new();
    public RecoilStats Recoil { get; set; } = RecoilStats.RevolverDefaults();

    public string BulletWildcard { get; set; } = "*bullet-*";
    public string FlaskWildcard { get; set; } = "*powderflask-*";
    public string WaddingWildcard { get; set; } = "*linenpatch*";
    public string LoadingRequirementWildcard { get; set; } = "";
    public string LoadingRequirementMessage { get; set; } = "maltiezfirearms:requirement-missing-loading-equipment";

    public bool CancelReloadOnInAir { get; set; } = true;
    public float ReloadAnimationSpeed { get; set; } = 1;
    public bool TwoHanded { get; set; } = true;
    public int BulletsLoadedPerBulletItem { get; set; } = 1;

    public void ApplyLoadStagesTemplate()
    {
        if (LoadStagesTemplate == null) return;

        for (int index = 0; index < LoadStages.Length; index++)
        {
            RevolverLoadStageStats currentStage = LoadStages[index];

            currentStage.LoadedAnimation = ApplyAnimationTemplate(index, LoadStagesTemplate.LoadedAnimation, currentStage.LoadedAnimation);
            currentStage.UnloadedAnimation = ApplyAnimationTemplate(index, LoadStagesTemplate.UnloadedAnimation, currentStage.UnloadedAnimation);
            currentStage.LoadingAnimation = ApplyAnimationTemplate(index, LoadStagesTemplate.LoadingAnimation, currentStage.LoadingAnimation);
            currentStage.CockingAnimation = ApplyAnimationTemplate(index, LoadStagesTemplate.CockingAnimation, currentStage.CockingAnimation);
            currentStage.CockedAnimation = ApplyAnimationTemplate(index, LoadStagesTemplate.CockedAnimation, currentStage.CockedAnimation);
            currentStage.ShootAnimation = ApplyAnimationTemplate(index, LoadStagesTemplate.ShootAnimation, currentStage.ShootAnimation);
            currentStage.FiredAnimation = ApplyAnimationTemplate(index, LoadStagesTemplate.FiredAnimation, currentStage.FiredAnimation);
            currentStage.AimAnimation = ApplyAnimationTemplate(index, LoadStagesTemplate.AimAnimation, currentStage.AimAnimation);
            currentStage.StartLoadingAnimation = ApplyAnimationTemplate(index, LoadStagesTemplate.StartLoadingAnimation, currentStage.StartLoadingAnimation);
            currentStage.ContinueLoadingAnimation = ApplyAnimationTemplate(index, LoadStagesTemplate.ContinueLoadingAnimation, currentStage.ContinueLoadingAnimation);
            currentStage.FinishLoadingAnimation = ApplyAnimationTemplate(index, LoadStagesTemplate.FinishLoadingAnimation, currentStage.FinishLoadingAnimation);
            currentStage.CancelLoadingAnimation = ApplyAnimationTemplate(index, LoadStagesTemplate.CancelLoadingAnimation, currentStage.CancelLoadingAnimation);
            currentStage.CylinderPositionAnimation = ApplyAnimationTemplate(index, LoadStagesTemplate.CylinderPositionAnimation, currentStage.CylinderPositionAnimation);
        }
    }

    protected static string ApplyAnimationTemplate(int index, string template, string animation)
    {
        if (template == "") return animation;

       return template.Replace("{index}", index.ToString());
    }
}

public class RevolverClient : RangeWeaponClient
{
    public RevolverClient(ICoreClientAPI api, Item item) : base(api, item)
    {
        Attachable = item.GetCollectibleBehavior<AnimatableAttachable>(withInheritance: true) ?? throw new Exception("Firearm should have AnimatableAttachable behavior.");
        AimingSystem = api.ModLoader.GetModSystem<CombatOverhaulSystem>().AimingSystem ?? throw new Exception();
        BulletTransform = new(item.Attributes["BulletTransform"].AsObject<ModelTransformNoDefaults>() ?? new ModelTransformNoDefaults(), ModelTransform.BlockDefaultTp());
        FlaskTransform = new(item.Attributes["FlaskTransform"].AsObject<ModelTransformNoDefaults>() ?? new ModelTransformNoDefaults(), ModelTransform.BlockDefaultTp());
        LoadingEquipmentTransform = new(item.Attributes["LoadingEquipmentTransform"].AsObject<ModelTransformNoDefaults>() ?? new ModelTransformNoDefaults(), ModelTransform.BlockDefaultTp());
        Stats = item.Attributes.AsObject<RevolverStats>();
        AimingStats = Stats.Aiming.ToStats();
        TwoHanded = Stats.TwoHanded;

        Stats.ApplyLoadStagesTemplate();

#if DEBUG
        DebugWindowManager.RegisterTransformByCode(BulletTransform, $"Bullet - {item.Code}");
        DebugWindowManager.RegisterTransformByCode(FlaskTransform, $"Flask - {item.Code}");
        DebugWindowManager.RegisterTransformByCode(LoadingEquipmentTransform, $"Loading - {item.Code}");
#endif

        FirearmsModSystem system = api.ModLoader.GetModSystem<FirearmsModSystem>();
        system.SettingsChanged += settings =>
        {
            AimingStats.CursorType = Enum.Parse<AimingCursorType>(settings.AimingCursorType, ignoreCase: true);
        };
        AimingStats.CursorType = Enum.Parse<AimingCursorType>(system.Settings.AimingCursorType, ignoreCase: true);

        //DebugWidgets.FloatDrag("test", "test", $"{item.Code}-followX", () => AimingStats.AnimationFollowX, (value) => AimingStats.AnimationFollowX = value);
        //DebugWidgets.FloatDrag("test", "test", $"{item.Code}-followY", () => AimingStats.AnimationFollowY, (value) => AimingStats.AnimationFollowY = value);
    }

    public override void OnSelected(ItemSlot slot, EntityPlayer player, bool mainHand, ref int state)
    {
        Attachable.ClearAttachments(player.EntityId);
        AimingSystem.AimingState = WeaponAimingState.None;

        CurrentLoadStage = GetLoadStage(slot);
        CurrentReadyState = GetReadyState(slot);

        RevolverLoadStageStats currentStage = Stats.LoadStages[CurrentLoadStage];

        AnimationBehavior?.PlayFpAndTp(mainHand, currentStage.CylinderPositionAnimation, category: CylinderPositionAnimationCategory, weight: 1.1f);
        AnimationBehavior?.PlayFpAndTp(mainHand, currentStage.LoadedAnimation, category: CylinderLoadGatesAnimationCategory, weight: 1.1f);

        switch (CurrentReadyState)
        {
            case RevolverReadyState.Unloaded:
                break;
            case RevolverReadyState.Ready:
                break;
            case RevolverReadyState.Fired:
                AnimationBehavior?.PlayFpAndTp(mainHand, currentStage.FiredAnimation, category: LockAnimationCategory, weight: 1.1f);
                break;
        }

#if DEBUG
        DebugAttach(player);
#endif
    }
    public override void OnDeselected(EntityPlayer player, bool mainHand, ref int state)
    {
        switch ((RevolverState)state)
        {
            case RevolverState.StartLoading:
            case RevolverState.Loading:
            case RevolverState.FinishLoading:
            case RevolverState.Cocking:
                ReloadActionId++;
                RangedWeaponSystem.SendStatusChange(player, RangedWeaponStatus.EndLoading, mainHand);
                break;
            case RevolverState.Aim:
            case RevolverState.Shoot:
                RangedWeaponSystem.SendStatusChange(player, RangedWeaponStatus.EndAiming, mainHand);
                break;
        }

        Attachable.ClearAttachments(player.EntityId);
        AimingAnimationController?.Stop(mainHand);
        AimingSystem.AimingState = WeaponAimingState.None;
        AimingSystem.StopAiming();
        PlayerBehavior?.SetStat("walkspeed", mainHand ? PlayerStatsMainHandCategory : PlayerStatsOffHandCategory);
    }
    public override void OnRegistered(ActionsManagerPlayerBehavior behavior, ICoreClientAPI api)
    {
        base.OnRegistered(behavior, api);
        AnimationBehavior = behavior.entity.GetBehavior<FirstPersonAnimationsBehavior>();
        TpAnimationBehavior = behavior.entity.GetBehavior<ThirdPersonAnimationsBehavior>();
        AimingAnimationController = new(AimingSystem, AnimationBehavior, AimingStats);
    }


    protected AimingAnimationController? AimingAnimationController;
    protected readonly AnimatableAttachable Attachable;
    protected readonly ClientAimingSystem AimingSystem;
    protected readonly RevolverStats Stats;
    protected readonly AimingStats AimingStats;
    protected long LastRecoilTimestampMs = -1000;
    protected long NextShotAllowedMainHandMs = 0;
    protected long NextShotAllowedOffHandMs = 0;
    protected readonly ItemInventoryBuffer Inventory = new();
    protected readonly ModelTransform BulletTransform;
    protected readonly ModelTransform FlaskTransform;
    protected readonly ModelTransform LoadingEquipmentTransform;
    protected readonly ModelTransform PrimingEquipmentTransform;
    protected new FirstPersonAnimationsBehavior? AnimationBehavior;
    protected new ThirdPersonAnimationsBehavior? TpAnimationBehavior;
    protected const string InventoryId = "magazine";
    protected const string LoadingStageAttribute = "CombatOverhaul:loading-stage";
    protected const string WeaponReadyStageAttribute = "CombatOverhaul:ready-stage";
    protected const string PlayerStatsMainHandCategory = "CombatOverhaul:held-item-mainhand";
    protected const string PlayerStatsOffHandCategory = "CombatOverhaul:held-item-offhand";
    protected const string LoadingGatesAnimationCategory = "loading-gates";
    protected const string LockAnimationCategory = "lock";
    protected const string CylinderPositionAnimationCategory = "cylinder-position";
    protected const string CylinderLoadGatesAnimationCategory = "cylinder-load-gates";
    protected int ReloadActionId = 0;

    protected ItemSlot? BulletSlot;
    protected ItemStack? BulletAttachmentStack;
    protected int CurrentLoadStage 
    { 
        get => _currentLoadStage;
        set
        {
            Debug.WriteLine($"Change stage: {_currentLoadStage} --> {value}");
            _currentLoadStage = value;
        }
    }
    private int _currentLoadStage;
    protected RevolverReadyState CurrentReadyState;


    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool StartLoad(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (!CheckState(state, RevolverState.Idle) || !mainHand) return false;
        if (!eventData.Modifiers.Contains(EnumEntityAction.CtrlKey) ||
            eventData.Modifiers.Contains(EnumEntityAction.Forward) ||
            eventData.Modifiers.Contains(EnumEntityAction.Backward) ||
            eventData.Modifiers.Contains(EnumEntityAction.Left) ||
            eventData.Modifiers.Contains(EnumEntityAction.Right)) return false;
        if (InteractionsTester.PlayerTriesToInteract(player, mainHand, eventData)) return false;
        if (eventData.AltPressed || !CheckForOtherHandEmpty(mainHand, player)) return false;

        RevolverReadyState readyStage = GetReadyState(slot);
        int maxLoadStage = Stats.LoadStages.Length - 1;

        if (readyStage == RevolverReadyState.Fired) return false;
        if (readyStage == RevolverReadyState.Ready && CurrentLoadStage == maxLoadStage) return false;

        RevolverLoadStageStats currentStage = Stats.LoadStages[CurrentLoadStage];

        if (!currentStage.CanLoad) return false;
        if (
            !CheckRequirement(Stats.LoadingRequirementWildcard, player) ||
            !CheckWadding(player, currentStage.WaddingUsed) ||
            !CheckFlask(player, currentStage.PowderConsumption) ||
            !CheckAmmo(player, currentStage.BulletLoaded))
        {
            return false;
        }

        int reloadActionId = ++ReloadActionId;

        AnimationBehavior?.StopFpAndTp(CylinderPositionAnimationCategory);
        AnimationBehavior?.PlayFpAndTp(
            mainHand,
            currentStage.StartLoadingAnimation,
            animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat, slot.Itemstack),
            category: AnimationCategory(mainHand),
            callback: () => StartLoadCallback(slot, player, mainHand, CurrentLoadStage, reloadActionId));

        SetState(RevolverState.StartLoading, mainHand);

        RangedWeaponSystem.SendStatusChange(player, RangedWeaponStatus.StartLoading, mainHand);

        FirearmsReloadSafety.SetReloadWalkspeedPenalty(PlayerBehavior, mainHand, PlayerStatsMainHandCategory, PlayerStatsOffHandCategory, currentStage.LoadSpeedPenalty);

        return true;
    }
    protected virtual bool StartLoadCallback(ItemSlot slot, EntityPlayer player, bool mainHand, int stage, int reloadActionId)
    {
        if (reloadActionId != ReloadActionId || !CheckState(mainHand, RevolverState.StartLoading)) return true;

        RevolverLoadStageStats currentStage = Stats.LoadStages[stage];

        if (PlayerBehavior?.ActionListener.IsActive(EnumEntityAction.RightMouseDown) == false)
        {
            AnimationBehavior?.PlayFpAndTp(
                mainHand,
                currentStage.LoadedAnimation,
                category: CylinderLoadGatesAnimationCategory,
                weight: 1.1f);

            AnimationBehavior?.PlayFpAndTp(
                    mainHand,
                    currentStage.CancelLoadingAnimation,
                    animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat, slot.Itemstack),
                    category: AnimationCategory(mainHand),
                    callback: () => FinishLoadCallback(slot, player, mainHand, CurrentLoadStage, reloadActionId));

            SetState(RevolverState.FinishLoading, mainHand);

            return true;
        }

        SetState(RevolverState.Loading, mainHand);

        int nextStageIndex = currentStage.LoadToStage;
        RevolverLoadStageStats nextStage = Stats.LoadStages[nextStageIndex];

        AnimationBehavior?.PlayFpAndTp(
            mainHand,
            nextStage.UnloadedAnimation,
            category: CylinderLoadGatesAnimationCategory,
            weight: 1.1f);

        AnimationBehavior?.PlayFpAndTp(
            mainHand,
            nextStage.LoadingAnimation,
            animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat, slot.Itemstack),
            category: AnimationCategory(mainHand),
            callback: () => LoadStageCallback(slot, player, mainHand, nextStageIndex, reloadActionId),
            callbackHandler: code => LoadStageCallbackHandler(code, slot, player, mainHand, nextStageIndex, reloadActionId));

        return true;
    }
    protected virtual bool LoadStageCallback(ItemSlot slot, EntityPlayer player, bool mainHand, int stage, int reloadActionId)
    {
        if (reloadActionId != ReloadActionId || !CheckState(mainHand, RevolverState.Loading)) return true;
        if (stage == CurrentLoadStage && CurrentReadyState == RevolverReadyState.Ready) return true;

        RevolverLoadStageStats currentStage = Stats.LoadStages[stage];

        bool success = SendReloadPacket(slot, player, mainHand, stage);

        if (!success)
        {
            RevolverLoadStageStats previousStage = Stats.LoadStages[currentStage.UnloadToStage];

            AnimationBehavior?.PlayFpAndTp(
                mainHand,
                previousStage.FinishLoadingAnimation,
                animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat, slot.Itemstack),
                category: AnimationCategory(mainHand),
                callback: () => FinishLoadCallback(slot, player, mainHand, stage, reloadActionId));
            AnimationBehavior?.PlayFpAndTp(
                mainHand,
                previousStage.LoadedAnimation,
                category: CylinderLoadGatesAnimationCategory,
                weight: 1.1f);

            SetState(RevolverState.FinishLoading, mainHand);

            return true;
        }

        CurrentLoadStage = stage;
        CurrentReadyState = RevolverReadyState.Ready;

        AnimationBehavior?.PlayFpAndTp(
                mainHand,
                currentStage.LoadedAnimation,
                category: CylinderLoadGatesAnimationCategory,
                weight: 1.1f);

        int nextStageIndex = currentStage.LoadToStage;

        RevolverLoadStageStats nextStage = Stats.LoadStages[nextStageIndex];

        if (
            !currentStage.CanLoad ||
            !CheckRequirement(Stats.LoadingRequirementWildcard, player) ||
            !CheckWadding(player, currentStage.WaddingUsed) ||
            !CheckFlask(player, currentStage.PowderConsumption) ||
            !CheckAmmo(player, currentStage.BulletLoaded))
        {
            AnimationBehavior?.PlayFpAndTp(
                mainHand,
                currentStage.FinishLoadingAnimation,
                animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat, slot.Itemstack),
                category: AnimationCategory(mainHand),
                callback: () => FinishLoadCallback(slot, player, mainHand, stage, reloadActionId));

            SetState(RevolverState.FinishLoading, mainHand);

            return true;
        }


        AnimationBehavior?.PlayFpAndTp(
            mainHand,
            currentStage.ContinueLoadingAnimation,
            animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat, slot.Itemstack),
            category: AnimationCategory(mainHand),
            callback: () => ContinueLoadCallback(slot, player, mainHand, nextStageIndex, reloadActionId));

        FirearmsReloadSafety.SetReloadWalkspeedPenalty(PlayerBehavior, mainHand, PlayerStatsMainHandCategory, PlayerStatsOffHandCategory, nextStage.LoadSpeedPenalty);

        return true;
    }
    protected virtual bool ContinueLoadCallback(ItemSlot slot, EntityPlayer player, bool mainHand, int stage, int reloadActionId)
    {
        if (reloadActionId != ReloadActionId || !CheckState(mainHand, RevolverState.Loading)) return true;

        Debug.WriteLine($"ContinueLoad ({CurrentLoadStage}|{CurrentReadyState})");

        RevolverLoadStageStats currentStage = Stats.LoadStages[stage];

        AnimationBehavior?.PlayFpAndTp(
            mainHand,
            currentStage.LoadingAnimation,
            animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat, slot.Itemstack),
            category: AnimationCategory(mainHand),
            callback: () => LoadStageCallback(slot, player, mainHand, stage, reloadActionId),
            callbackHandler: code => LoadStageCallbackHandler(code, slot, player, mainHand, stage, reloadActionId));

        return true;
    }
    protected virtual void LoadStageCallbackHandler(string code, ItemSlot slot, EntityPlayer player, bool mainHand, int stage, int reloadActionId)
    {
        if (reloadActionId != ReloadActionId || !CheckState(mainHand, RevolverState.Loading)) return;

        switch (code)
        {
            case "attach":
                {
                    ItemStack? bulletAttachment = GetBulletAttachmentStack();
                    if (bulletAttachment != null) Attachable.SetAttachment(player.EntityId, "bullet", bulletAttachment, BulletTransform);

                    ItemSlot? flaskSlot = null;
                    player.WalkInventory(slot =>
                    {
                        if (slot?.Itemstack?.Item == null) return true;

                        if (WildcardUtil.Match(Stats.FlaskWildcard, slot.Itemstack.Item.Code.ToString()))
                        {
                            flaskSlot = slot;
                            return false;
                        }

                        return true;
                    });
                    if (flaskSlot != null) Attachable.SetAttachment(player.EntityId, "flask", flaskSlot.Itemstack, FlaskTransform);

                    /*ItemSlot? loadingSlot = null;
                    player.WalkInventory(slot =>
                    {
                        if (slot?.Itemstack?.Item == null) return true;

                        if (WildcardUtil.Match(Stats.LoadingRequirementWildcard, slot.Itemstack.Item.Code.ToString()))
                        {
                            loadingSlot = slot;
                            return false;
                        }

                        return true;
                    });
                    if (loadingSlot != null) Attachable.SetAttachment(player.EntityId, "loading", loadingSlot.Itemstack, LoadingEquipmentTransform);*/
                }
                break;
            case "detach":
                {
                    Attachable.ClearAttachments(player.EntityId);
                }
                break;
        }
    }
    protected virtual bool FinishLoadCallback(ItemSlot slot, EntityPlayer player, bool mainHand, int stage, int reloadActionId)
    {
        if (reloadActionId != ReloadActionId || !CheckState(mainHand, RevolverState.FinishLoading)) return true;

        Debug.WriteLine($"FinishLoad ({CurrentLoadStage}|{CurrentReadyState})");

        SetState(RevolverState.Idle, mainHand);

        RangedWeaponSystem.SendStatusChange(player, RangedWeaponStatus.EndLoading, mainHand);

        RevolverLoadStageStats currentStage = Stats.LoadStages[stage];
        AnimationBehavior?.PlayFpAndTp(mainHand, currentStage.CylinderPositionAnimation, category: CylinderPositionAnimationCategory, weight: 1.1f);
        AnimationBehavior?.PlayReadyAnimationFpAndTp(mainHand);

        PlayerBehavior?.SetStat("walkspeed", mainHand ? PlayerStatsMainHandCategory : PlayerStatsOffHandCategory);

        Attachable.ClearAttachments(player.EntityId);

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool Aim(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (!CheckState(state, RevolverState.Idle) || !mainHand) return false;
        if (InteractionsTester.PlayerTriesToInteract(player, mainHand, eventData)) return false;
        if (eventData.AltPressed || !CheckForOtherHandEmpty(mainHand, player)) return false;

        Debug.WriteLine($"Aim ({CurrentLoadStage}|{CurrentReadyState})");

        RevolverLoadStageStats currentStage = Stats.LoadStages[CurrentLoadStage];

        AnimationBehavior?.PlayFpAndTp(
            mainHand,
            currentStage.AimAnimation,
            category: AnimationCategory(mainHand),
            callback: () => AimCallback(slot, player, mainHand, CurrentLoadStage));

        SetState(RevolverState.Aim, mainHand);

        AimingSystem.StartAiming(GetAimStats(slot));
        AimingSystem.AimingState = WeaponAimingState.FullCharge;
        AimingAnimationController?.Play(mainHand);

        RangedWeaponSystem.SendStatusChange(player, RangedWeaponStatus.StartAiming, mainHand);

        return true;
    }
    protected virtual bool AimCallback(ItemSlot slot, EntityPlayer player, bool mainHand, int stage)
    {
        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Released)]
    protected virtual bool CancelLoading(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (!CheckState(state, RevolverState.Loading) || !mainHand) return false;
        if (eventData.AltPressed) return false;

        Debug.WriteLine($"CancelLoading ({CurrentLoadStage}|{CurrentReadyState})");

        RevolverLoadStageStats currentStage = Stats.LoadStages[CurrentLoadStage];
        int reloadActionId = ReloadActionId;

        AnimationBehavior?.PlayFpAndTp(
                mainHand,
                currentStage.LoadedAnimation,
                category: CylinderLoadGatesAnimationCategory,
                weight: 1.1f);

        AnimationBehavior?.PlayFpAndTp(
                mainHand,
                currentStage.CancelLoadingAnimation,
                animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat, slot.Itemstack),
                category: AnimationCategory(mainHand),
                callback: () => FinishLoadCallback(slot, player, mainHand, CurrentLoadStage, reloadActionId));

        SetState(RevolverState.FinishLoading, mainHand);

        RangedWeaponSystem.SendStatusChange(player, RangedWeaponStatus.EndLoading, mainHand);

        PlayerBehavior?.SetStat("walkspeed", mainHand ? PlayerStatsMainHandCategory : PlayerStatsOffHandCategory);

        Attachable.ClearAttachments(player.EntityId);

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Released)]
    protected virtual bool Ease(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (!CheckState(state, RevolverState.Aim) || !mainHand) return false;
        if (eventData.AltPressed) return false;

        Debug.WriteLine($"Ease");

        AnimationBehavior?.PlayReadyAnimationFpAndTp(mainHand);
        AimingSystem.StopAiming();
        AimingAnimationController?.Stop(mainHand);

        SetState(RevolverState.Idle, mainHand);

        RangedWeaponSystem.SendStatusChange(player, RangedWeaponStatus.EndAiming, mainHand);

        return true;
    }

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Pressed)]
    protected virtual bool Shoot(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
        => TryShoot(slot, player, ref state, eventData, mainHand);

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Active)]
    protected virtual bool ShootAutomatic(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (!Stats.AutomaticFire) return false;

        // In automatic mode holding trigger advances the whole firing loop:
        // fire if chamber is ready, otherwise cock to next chamber.
        if (CurrentReadyState == RevolverReadyState.Ready)
        {
            return TryShoot(slot, player, ref state, eventData, mainHand);
        }

        if (CurrentReadyState == RevolverReadyState.Fired)
        {
            return TryCock(slot, player, ref state, eventData, mainHand);
        }

        return false;
    }

    protected bool ShouldContinueAutomaticFire(bool mainHand) =>
        Stats.AutomaticFire &&
        PlayerBehavior?.ActionListener.IsActive(EnumEntityAction.LeftMouseDown) == true &&
        PlayerBehavior?.ActionListener.IsActive(EnumEntityAction.RightMouseDown) == true;

    protected virtual bool TryShoot(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand)
    {
        if (CurrentReadyState != RevolverReadyState.Ready) return false;
        if (!CheckState(state, RevolverState.Aim) || !mainHand) return false;
        if (!ShotGateOpen(mainHand)) return false;
        if (InteractionsTester.PlayerTriesToInteract(player, mainHand, eventData)) return false;
        if (eventData.AltPressed || !CheckForOtherHandEmpty(mainHand, player)) return false;

        RevolverLoadStageStats currentStage = Stats.LoadStages[CurrentLoadStage];

        if (!currentStage.CanFire) return false;

        SetState(RevolverState.Shoot, mainHand);

        CurrentReadyState = RevolverReadyState.Fired;

        SendWeaponState(CurrentLoadStage, RevolverReadyState.Fired, slot, player, mainHand);

        Debug.WriteLine($"Shoot ({CurrentLoadStage}|{CurrentReadyState})");

        RangedWeaponSystem.SendStatusChange(player, RangedWeaponStatus.TriggeredShot, mainHand);
        SetNextShotAllowed(mainHand, Api.World.ElapsedMilliseconds + GetActionDelayMs(player, currentStage.ShootAnimation, GetAnimationSpeed(player, Stats.ProficiencyStat, slot.Itemstack), 500));

        AnimationBehavior?.PlayFpAndTp(
                mainHand,
                currentStage.UnloadedAnimation,
                category: CylinderLoadGatesAnimationCategory,
                weight: 1.1f);
        AnimationBehavior?.StopFpAndTp(CylinderPositionAnimationCategory);
        AnimationBehavior?.StopFpAndTp(LockAnimationCategory);

        AnimationBehavior?.PlayFpAndTp(
            mainHand,
            currentStage.ShootAnimation,
            category: AnimationCategory(mainHand),
            callback: () => ShootCallback(slot, player, mainHand),
            callbackHandler: callback => ShootAnimationCallback(callback, slot, player, mainHand, CurrentLoadStage));

        return true;
    }

    protected virtual bool TryShootAutomaticContinuation(ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        if (CurrentReadyState != RevolverReadyState.Ready) return false;
        if (!CheckState(mainHand, RevolverState.Aim) || !mainHand) return false;
        if (!ShotGateOpen(mainHand)) return false;
        if (!CheckForOtherHandEmpty(mainHand, player)) return false;

        RevolverLoadStageStats currentStage = Stats.LoadStages[CurrentLoadStage];

        if (!currentStage.CanFire) return false;

        SetState(RevolverState.Shoot, mainHand);

        CurrentReadyState = RevolverReadyState.Fired;

        SendWeaponState(CurrentLoadStage, RevolverReadyState.Fired, slot, player, mainHand);

        Debug.WriteLine($"Shoot ({CurrentLoadStage}|{CurrentReadyState})");

        RangedWeaponSystem.SendStatusChange(player, RangedWeaponStatus.TriggeredShot, mainHand);
        SetNextShotAllowed(mainHand, Api.World.ElapsedMilliseconds + GetActionDelayMs(player, currentStage.ShootAnimation, GetAnimationSpeed(player, Stats.ProficiencyStat, slot.Itemstack), 500));

        AnimationBehavior?.PlayFpAndTp(
                mainHand,
                currentStage.UnloadedAnimation,
                category: CylinderLoadGatesAnimationCategory,
                weight: 1.1f);
        AnimationBehavior?.StopFpAndTp(CylinderPositionAnimationCategory);
        AnimationBehavior?.StopFpAndTp(LockAnimationCategory);

        AnimationBehavior?.PlayFpAndTp(
            mainHand,
            currentStage.ShootAnimation,
            category: AnimationCategory(mainHand),
            callback: () => ShootCallback(slot, player, mainHand),
            callbackHandler: callback => ShootAnimationCallback(callback, slot, player, mainHand, CurrentLoadStage));

        return true;
    }

    protected bool ShotGateOpen(bool mainHand) => Api.World.ElapsedMilliseconds >= GetNextShotAllowed(mainHand);

    protected long GetNextShotAllowed(bool mainHand) => mainHand ? NextShotAllowedMainHandMs : NextShotAllowedOffHandMs;

    protected void SetNextShotAllowed(bool mainHand, long value)
    {
        if (mainHand)
        {
            NextShotAllowedMainHandMs = value;
        }
        else
        {
            NextShotAllowedOffHandMs = value;
        }
    }

    protected virtual bool ShootCallback(ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        RevolverLoadStageStats currentStage = Stats.LoadStages[CurrentLoadStage];

        AnimationBehavior?.PlayFpAndTp(mainHand, currentStage.CylinderPositionAnimation, category: CylinderPositionAnimationCategory, weight: 1.1f);
        AnimationBehavior?.PlayFpAndTp(mainHand, currentStage.FiredAnimation, category: LockAnimationCategory, weight: 1.1f);

        if (PlayerBehavior?.ActionListener.IsActive(EnumEntityAction.RightMouseDown) == false)
        {
            AnimationBehavior?.PlayReadyAnimationFpAndTp(mainHand);
            AimingSystem.StopAiming();
            AimingAnimationController?.Stop(mainHand);

            SetState(RevolverState.Idle, mainHand);

            RangedWeaponSystem.SendStatusChange(player, RangedWeaponStatus.EndAiming, mainHand);

            return true;
        }

        SetState(RevolverState.Aim, mainHand);

        if (ShouldContinueAutomaticFire(mainHand) && TryCockAutomaticContinuation(slot, player, mainHand))
        {
            return true;
        }

        AnimationBehavior?.PlayFpAndTp(
            mainHand,
            currentStage.AimAnimation,
            animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat, slot.Itemstack),
            category: AnimationCategory(mainHand),
            callback: () => AimCallback(slot, player, mainHand, CurrentLoadStage));

        AimingAnimationController?.Play(mainHand);

        return true;
    }
    protected virtual void ShootAnimationCallback(string callback, ItemSlot slot, EntityPlayer player, bool mainHand, int stage)
    {
        switch (callback)
        {
            case "shoot":
                RangedWeaponSystem.SendStatusChange(player, RangedWeaponStatus.SpawnedProjectile, mainHand);
                SendShootPacket(slot, player, mainHand, stage);
                ApplyRecoil(slot, player, stage);
                break;
        }
    }

    protected virtual void ApplyRecoil(ItemSlot slot, EntityPlayer player, int stage)
    {
        if (!FirearmsAmmoUtility.RecoilEnabled) return;

        if (Api.World.Player?.Entity?.EntityId != player.EntityId) return;
        long now = Api.World.ElapsedMilliseconds;
        if (now - LastRecoilTimestampMs < 80) return;
        LastRecoilTimestampMs = now;

        RecoilStats recoil = Stats.Recoil ?? RecoilStats.RevolverDefaults();
        if (!recoil.Enabled) return;

        float estimatedDamage = EstimateShotDamage(slot, stage);
        float verticalDeg = recoil.VerticalDegrees(estimatedDamage);
        float horizontalDeg = recoil.HorizontalDegrees(estimatedDamage, verticalDeg);

        FirearmsRecoilSystem.AddRecoil(verticalDeg, horizontalDeg, recoil);
        Api.World.AddCameraShake(recoil.CameraShake(estimatedDamage));
    }

    protected virtual float EstimateShotDamage(ItemSlot slot, int stage)
    {
        ItemStackRangedStats stackStats = ItemStackRangedStats.FromItemStack(slot.Itemstack);
        float projectileBaseDamage = 1.0f;

        Inventory.Read(slot, InventoryId);
        if (Inventory.Items.Count > 0)
        {
            ItemStack ammo = Inventory.Items[0];
            ProjectileStats? projectileStats = ammo.Item?.GetCollectibleBehavior<ProjectileBehavior>(true)?.GetStats(ammo);
            if (projectileStats?.DamageStats != null)
            {
                projectileBaseDamage = projectileStats.DamageStats.Damage;
            }
        }
        Inventory.Clear();

        float weaponMultiplier = Math.Max(0.1f, Stats.FiringStats.BulletDamageMultiplier * stackStats.DamageMultiplier);

        // Do not multiply recoil by stage projectile count.
        // Multishot stages already fire multiple projectiles and otherwise hit the pitch clamp instantly.
        return Math.Max(0.1f, projectileBaseDamage * weaponMultiplier);
    }

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Pressed)]
    protected virtual bool Cock(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
        => TryCock(slot, player, ref state, eventData, mainHand);

    protected virtual bool TryCock(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand)
    {
        if (CurrentReadyState != RevolverReadyState.Fired) return false;
        if (!CheckState(state, RevolverState.Aim) || !mainHand) return false;
        if (InteractionsTester.PlayerTriesToInteract(player, mainHand, eventData)) return false;
        if (eventData.AltPressed || !CheckForOtherHandEmpty(mainHand, player)) return false;

        Debug.WriteLine($"Cock ({CurrentLoadStage}|{CurrentReadyState})");

        RevolverLoadStageStats currentStage = Stats.LoadStages[CurrentLoadStage];

        SetState(RevolverState.Cocking, mainHand);

        AnimationBehavior?.StopFpAndTp(CylinderPositionAnimationCategory);
        AnimationBehavior?.StopFpAndTp(LockAnimationCategory);
        AnimationBehavior?.PlayFpAndTp(
            mainHand,
            currentStage.CockingAnimation,
            animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat, slot.Itemstack),
            category: AnimationCategory(mainHand),
            callback: () => CockCallback(slot, player, mainHand));

        AimingSystem.StopAiming();
        AimingAnimationController?.Stop(mainHand);

        return true;
    }

    protected virtual bool TryCockAutomaticContinuation(ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        if (CurrentReadyState != RevolverReadyState.Fired) return false;
        if (!CheckState(mainHand, RevolverState.Aim) || !mainHand) return false;
        if (!CheckForOtherHandEmpty(mainHand, player)) return false;

        Debug.WriteLine($"Cock ({CurrentLoadStage}|{CurrentReadyState})");

        RevolverLoadStageStats currentStage = Stats.LoadStages[CurrentLoadStage];

        SetState(RevolverState.Cocking, mainHand);

        AnimationBehavior?.StopFpAndTp(CylinderPositionAnimationCategory);
        AnimationBehavior?.StopFpAndTp(LockAnimationCategory);
        AnimationBehavior?.PlayFpAndTp(
            mainHand,
            currentStage.CockingAnimation,
            animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat, slot.Itemstack),
            category: AnimationCategory(mainHand),
            callback: () => CockCallback(slot, player, mainHand));

        AimingSystem.StopAiming();
        AimingAnimationController?.Stop(mainHand);

        return true;
    }

    protected virtual bool CockCallback(ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        SetState(RevolverState.Aim, mainHand);

        RevolverLoadStageStats currentStage = Stats.LoadStages[CurrentLoadStage];
        CurrentLoadStage = currentStage.UnloadToStage;
        RevolverLoadStageStats nextStage = Stats.LoadStages[CurrentLoadStage];

        if (nextStage.CanFire)
        {
            CurrentReadyState = RevolverReadyState.Ready;
            SendWeaponState(CurrentLoadStage, RevolverReadyState.Ready, slot, player, mainHand);
        }
        else
        {
            CurrentReadyState = RevolverReadyState.Unloaded;
            SendWeaponState(CurrentLoadStage, RevolverReadyState.Unloaded, slot, player, mainHand);
        }

        AnimationBehavior?.PlayFpAndTp(mainHand, nextStage.CylinderPositionAnimation, category: CylinderPositionAnimationCategory, weight: 1.1f);
        AnimationBehavior?.PlayFpAndTp(mainHand, nextStage.CockedAnimation, category: LockAnimationCategory, weight: 1.1f);

        if (PlayerBehavior?.ActionListener.IsActive(EnumEntityAction.RightMouseDown) == false)
        {
            AnimationBehavior?.PlayReadyAnimationFpAndTp(mainHand);
            AimingSystem.StopAiming();
            AimingAnimationController?.Stop(mainHand);

            SetState(RevolverState.Idle, mainHand);

            return true;
        }

        AimingSystem.StartAiming(GetAimStats(slot));
        AimingSystem.AimingState = WeaponAimingState.FullCharge;
        AimingAnimationController?.Play(mainHand);

        if (ShouldContinueAutomaticFire(mainHand) && TryShootAutomaticContinuation(slot, player, mainHand))
        {
            return true;
        }

        AnimationBehavior?.PlayFpAndTp(
            mainHand,
            nextStage.AimAnimation,
            category: AnimationCategory(mainHand),
            callback: () => AimCallback(slot, player, mainHand, CurrentLoadStage));

        return true;
    }


    protected virtual bool SendReloadPacket(ItemSlot slot, EntityPlayer player, bool mainHand, int stage)
    {
        ItemSlot? ammoSlot = null;

        RevolverLoadStageStats currentStage = Stats.LoadStages[stage];

        player.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Item == null) return true;

            if (
                slot.Itemstack?.Item != null &&
                slot.Itemstack.Item.GetCollectibleBehavior<ProjectileBehavior>(true) != null &&
                WildcardUtil.Match(Stats.BulletWildcard, slot.Itemstack.Item.Code.ToString()) &&
                FirearmsAmmoUtility.HasEnoughBulletItems(slot, currentStage.BulletLoaded, Stats.BulletsLoadedPerBulletItem))
            {
                ammoSlot = slot;
                return false;
            }

            return true;
        });

        if (ammoSlot == null) return true;

        int bulletItemsRequired = FirearmsAmmoUtility.BulletItemsRequired(currentStage.BulletLoaded, Stats.BulletsLoadedPerBulletItem);
        int expectedStage = CurrentLoadStage;
        RevolverReadyState expectedReadyState = CurrentReadyState;

        RangedWeaponSystem.Reload(slot, ammoSlot, bulletItemsRequired, mainHand, success => LoadServerCallback(success, stage, mainHand, player), data: [(byte)stage, (byte)RevolverReadyState.Ready, (byte)expectedStage, (byte)expectedReadyState]);

        return true;
    }
    protected virtual void SendWeaponState(int stage, RevolverReadyState state, ItemSlot slot, EntityPlayer player, bool mainHand)
    {
        RangedWeaponSystem.Reload(slot, slot, 0, mainHand, success => { }, data: [(byte)stage, (byte)state]);
    }
    protected virtual void LoadServerCallback(bool success, int stage, bool mainHand, EntityPlayer player)
    {
        if (!success)
        {
            Debug.WriteLine($"Reload failed");
            // @TODO implement failed reload handling
        }
    }
    protected virtual void SendShootPacket(ItemSlot slot, EntityPlayer player, bool mainHand, int stage)
    {
        RevolverLoadStageStats currentStage = Stats.LoadStages[stage];

        Vintagestory.API.MathTools.Vec3d position = player.LocalEyePos + player.Pos.XYZ;
        Vector3 targetDirection = AimingSystem.TargetVec;

        targetDirection = ClientAimingSystem.Zeroing(targetDirection, Stats.FiringStats.Zeroing);

        RangedWeaponSystem.Shoot(
            slot,
            currentStage.BulletsFired,
            new((float)position.X, (float)position.Y, (float)position.Z),
            new(targetDirection.X, targetDirection.Y, targetDirection.Z),
            mainHand,
            ShootServerCallback,
            data: [(byte)stage, (byte)RevolverReadyState.Fired]);
    }
    protected virtual void ShootServerCallback(bool success)
    {

    }


    protected AimingStats GetAimStats(ItemSlot slot)
    {
        ItemStackRangedStats stackStats = ItemStackRangedStats.FromItemStack(slot.Itemstack);
        AimingStats aimingStats = AimingStats.Clone();
        aimingStats.AimDifficulty *= stackStats.AimingDifficulty;
        return aimingStats;
    }
    protected int GetLoadStage(ItemSlot slot)
    {
        return slot.Itemstack?.Attributes.GetAsInt(LoadingStageAttribute, 0) ?? 0;
    }
    protected RevolverReadyState GetReadyState(ItemSlot slot)
    {
        return (RevolverReadyState)(slot.Itemstack?.Attributes.GetAsInt(WeaponReadyStageAttribute, 0) ?? 0);
    }
    protected bool CheckRequirement(string requirementWildcard, EntityPlayer player)
    {
        if (requirementWildcard == "") return true;

        ItemSlot? requirementSlot = null;
        player.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Item == null) return true;

            if (WildcardUtil.Match(requirementWildcard, slot.Itemstack.Item.Code.ToString()))
            {
                requirementSlot = slot;
                return false;
            }

            return true;
        });

        if (requirementSlot == null)
        {
            Api.TriggerIngameError(this, "missingRequirement", Lang.Get(Stats.LoadingRequirementMessage));
        }

        return requirementSlot != null;
    }
    protected bool CheckWadding(EntityPlayer player, int amount)
    {
        if (amount <= 0) return true;

        ItemSlot? waddingSlot = null;
        player.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Item?.Code == null) return true;

            if (
                WildcardUtil.Match(Stats.WaddingWildcard, slot.Itemstack.Item.Code.ToString()) &&
                slot.Itemstack.StackSize >= amount)
            {
                waddingSlot = slot;
                return false;
            }

            return true;
        });

        bool found = waddingSlot != null;
        if (!found)
        {
            Api.TriggerIngameError(this, "missingWadding", Lang.Get("maltiezfirearms:requirement-missing-wadding"));
        }
        return found;
    }
    protected bool CheckFlask(EntityPlayer player, int powderNeeded)
    {
        if (powderNeeded <= 0) return true;

        ItemSlot? flaskSlot = null;
        player.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Item?.Code == null) return true;

            if (
                WildcardUtil.Match(Stats.FlaskWildcard, slot.Itemstack.Item.Code.ToString()) &&
                slot.Itemstack.Item.GetRemainingDurability(slot.Itemstack) >= powderNeeded)
            {
                flaskSlot = slot;
                return false;
            }

            return true;
        });

        bool found = flaskSlot != null;
        if (!found)
        {
            Api.TriggerIngameError(this, "missingPowderflask", Lang.Get("maltiezfirearms:requirement-missing-powderflask"));
        }
        return found;
    }
    protected bool CheckAmmo(EntityPlayer player, int amount)
    {
        ItemSlot? ammoSlot = null;
        player.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Item == null) return true;

            if (
                slot.Itemstack?.Item != null &&
                slot.Itemstack.Item.GetCollectibleBehavior<ProjectileBehavior>(true) != null &&
                WildcardUtil.Match(Stats.BulletWildcard, slot.Itemstack.Item.Code.ToString()) &&
                FirearmsAmmoUtility.HasEnoughBulletItems(slot, amount, Stats.BulletsLoadedPerBulletItem))
            {
                ammoSlot = slot;
                return false;
            }

            return true;
        });

        if (ammoSlot == null)
        {
            Api.TriggerIngameError(this, "missingAmmo", Lang.Get("maltiezfirearms:requirement-missing-ammo"));
            return false;
        }

        BulletSlot = ammoSlot;
        BulletAttachmentStack = CreateAttachmentStack(ammoSlot.Itemstack);

        return true;
    }
    protected ItemStack? GetBulletAttachmentStack()
    {
        if (BulletSlot?.Itemstack?.Item != null &&
            BulletSlot.Itemstack.Item.GetCollectibleBehavior<ProjectileBehavior>(true) != null &&
            WildcardUtil.Match(Stats.BulletWildcard, BulletSlot.Itemstack.Item.Code.ToString()))
        {
            BulletAttachmentStack = CreateAttachmentStack(BulletSlot.Itemstack);
        }

        return BulletAttachmentStack?.Clone();
    }
    protected static ItemStack? CreateAttachmentStack(ItemStack? stack)
    {
        if (stack?.Item == null) return null;

        ItemStack attachment = stack.Clone();
        attachment.StackSize = 1;
        return attachment;
    }
    protected ItemSlot? GetAmmoSlot(EntityPlayer player, int amount)
    {
        ItemSlot? ammoSlot = null;
        player.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Item == null) return true;

            if (
                slot.Itemstack?.Item != null &&
                slot.Itemstack.Item.GetCollectibleBehavior<ProjectileBehavior>(true) != null &&
                WildcardUtil.Match(Stats.BulletWildcard, slot.Itemstack.Item.Code.ToString()) &&
                FirearmsAmmoUtility.HasEnoughBulletItems(slot, amount, Stats.BulletsLoadedPerBulletItem))
            {
                ammoSlot = slot;
                return false;
            }

            return true;
        });

        if (ammoSlot == null)
        {
            Api.TriggerIngameError(this, "missingAmmo", Lang.Get("maltiezfirearms:requirement-missing-ammo"));
            return null;
        }

        return ammoSlot;
    }
    protected string AnimationCategory(bool mainHand) => mainHand ? "main" : "mainOffhand";
    protected string ItemAnimationCategory(bool mainHand) => mainHand ? "item" : "itemOffhand";
    protected void DebugAttach(EntityPlayer player)
    {
        ItemSlot? ammoSlot1 = null;
        player.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Item?.Code == null) return true;

            if (WildcardUtil.Match(Stats.BulletWildcard, slot.Itemstack.Item.Code.ToString()))
            {
                ammoSlot1 = slot;
                return false;
            }

            return true;
        });
        if (ammoSlot1 != null) Attachable.SetAttachment(player.EntityId, "bullet", ammoSlot1.Itemstack, BulletTransform);

        ItemSlot? ammoSlot2 = null;
        player.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Item?.Code == null) return true;

            if (WildcardUtil.Match(Stats.FlaskWildcard, slot.Itemstack.Item.Code.ToString()))
            {
                ammoSlot2 = slot;
                return false;
            }

            return true;
        });
        if (ammoSlot2 != null) Attachable.SetAttachment(player.EntityId, "flask", ammoSlot2.Itemstack, FlaskTransform);

        /*ItemSlot? ammoSlot3 = null;
        player.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Item == null) return true;

            if (WildcardUtil.Match(Stats.WaddingWildcard, slot.Itemstack.Item.Code.ToString()))
            {
                ammoSlot3 = slot;
                return false;
            }

            return true;
        });
        if (ammoSlot3 != null) Attachable.SetAttachment(player.EntityId, "wadding", ammoSlot3.Itemstack, WaddingTransform);*/

        /*ItemSlot? ammoSlot4 = null;
        player.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Item?.Code == null) return true;

            if (WildcardUtil.Match(Stats.PrimingRequirementWildcard, slot.Itemstack.Item.Code.ToString()))
            {
                ammoSlot4 = slot;
                return false;
            }

            return true;
        });
        if (ammoSlot4 != null) Attachable.SetAttachment(player.EntityId, "priming", ammoSlot4.Itemstack, PrimingEquipmentTransform);*/
    }
    protected float GetAnimationSpeed(EntityPlayer player, string stat, ItemStack stack)
    {
        ItemStackRangedStats stackStats = ItemStackRangedStats.FromItemStack(stack);
        return FirearmsReloadSafety.AnimationSpeed(GetAnimationSpeed(player, stat) * stackStats.ReloadSpeed * Stats.ReloadAnimationSpeed);
    }
}

public class RevolverServer : RangeWeaponServer
{
    public RevolverServer(ICoreServerAPI api, Item item) : base(api, item)
    {
        Stats = item.Attributes.AsObject<RevolverStats>();
    }

    public override bool Reload(IServerPlayer player, ItemSlot slot, ItemSlot? ammoSlot, ReloadPacket packet)
    {
        base.Reload(player, slot, ammoSlot, packet);

        if (packet.Data.Length < 2) return false;

        int stage = packet.Data[0];
        RevolverReadyState state = (RevolverReadyState)packet.Data[1];

        if (stage < 0 || stage >= Stats.LoadStages.Length) return false;

        if (packet.Amount > 0)
        {
            if (ammoSlot == null || packet.Data.Length < 4) return false;

            int expectedStage = packet.Data[2];
            RevolverReadyState expectedReadyState = (RevolverReadyState)packet.Data[3];

            int actualStage = slot.Itemstack?.Attributes.GetAsInt(LoadingStageAttribute, 0) ?? 0;
            RevolverReadyState actualReadyState = (RevolverReadyState)(slot.Itemstack?.Attributes.GetAsInt(WeaponReadyStageAttribute, 0) ?? 0);

            if (actualStage != expectedStage || actualReadyState != expectedReadyState)
            {
                return false;
            }
        }

        if (packet.Amount <= 0)
        {
            SetLoadStage(slot, stage);
            SetReadyState(slot, state);
            slot.MarkDirty();
            return true;
        }

        RevolverLoadStageStats currentStage = Stats.LoadStages[stage];

        int powderNeeded = currentStage.PowderConsumption;
        ItemSlot? flask = powderNeeded > 0 ? GetFlask(player, powderNeeded) : null;
        ItemSlot? wadding = currentStage.WaddingUsed > 0 ? GetWadding(player, currentStage.WaddingUsed) : null;

        if ((powderNeeded > 0 && flask?.Itemstack?.Item == null) ||
            (currentStage.WaddingUsed > 0 && wadding?.Itemstack?.Item == null)) return false;

        if (ammoSlot != null)
        {
            Inventory.Read(slot, InventoryId);

            if (
                ammoSlot.Itemstack?.Item?.Code != null &&
                ammoSlot.Itemstack.Item.GetCollectibleBehavior<ProjectileBehavior>(true) != null &&
                WildcardUtil.Match(Stats.BulletWildcard, ammoSlot.Itemstack.Item.Code.ToString()) &&
                FirearmsAmmoUtility.HasEnoughBulletItems(ammoSlot, currentStage.BulletLoaded, Stats.BulletsLoadedPerBulletItem))
            {
                FirearmsAmmoUtility.TryConsumeAndLoadBullets(Inventory, ammoSlot, currentStage.BulletLoaded, Stats.BulletsLoadedPerBulletItem);

                ammoSlot.MarkDirty();
                Inventory.Write(slot);
                Inventory.Clear();
            }
            else
            {
                Inventory.Clear();
                return false;
            }
        }

        if (flask != null && powderNeeded > 0)
        {
            flask.Itemstack.Attributes.SetInt("durability", flask.Itemstack.Collectible.GetRemainingDurability(flask.Itemstack) - powderNeeded);
            flask.MarkDirty();
        }

        if (wadding != null && currentStage.WaddingUsed > 0)
        {
            wadding.TakeOut(currentStage.WaddingUsed);
            wadding.MarkDirty();
        }

        SetLoadStage(slot, stage);
        SetReadyState(slot, state);
        slot.MarkDirty();

        return true;
    }

    public override bool Shoot(IServerPlayer player, ItemSlot slot, ShotPacket packet, Entity shooter)
    {
        base.Shoot(player, slot, packet, shooter);

        if (packet.Data.Length < 2) return false;

        int stage = packet.Data[0];
        RevolverReadyState state = (RevolverReadyState)packet.Data[1];
        RevolverLoadStageStats currentStage = Stats.LoadStages[stage];

        Inventory.Read(slot, InventoryId);

        if (Inventory.Items.Count == 0) return false;

        int count = 0;
        int additionalDurabilityCost = 0;
        while (Inventory.Items.Count > 0 && count < packet.Amount)
        {
            ItemStack ammo = Inventory.Items[0];
            ammo.ResolveBlockOrItem(Api.World);
            Inventory.Items.RemoveAt(0);

            ProjectileStats? stats = ammo.Item?.GetCollectibleBehavior<ProjectileBehavior>(true)?.GetStats(ammo);
            if (stats != null) FirearmsAmmoUtility.ApplyProjectileDisappearance(ammo, stats);

            if (stats == null)
            {
                continue;
            }

            ItemStackRangedStats stackStats = ItemStackRangedStats.FromItemStack(slot.Itemstack);

            additionalDurabilityCost = Math.Max(additionalDurabilityCost, stats.AdditionalDurabilityCost);

            float[] dispersion = [Stats.FiringStats.DispersionMOA[0] * stackStats.DispersionMultiplier, Stats.FiringStats.DispersionMOA[1] * stackStats.DispersionMultiplier];
            Vector3d velocity = GetDirectionWithDispersion(packet.Velocity, dispersion) * Stats.FiringStats.BulletVelocity * stackStats.ProjectileSpeed;

            ProjectileSpawnStats spawnStats = new()
            {
                ProducerEntityId = player.Entity.EntityId,
                DamageMultiplier = Stats.FiringStats.BulletDamageMultiplier * stackStats.DamageMultiplier,
                DamageTier = Stats.FiringStats.BulletDamageTier + stackStats.DamageTierBonus,
                Position = new Vector3d(packet.Position[0], packet.Position[1], packet.Position[2]),
                Velocity = velocity,
            };

            ProjectileSystem.Spawn(packet.ProjectileId[count], stats, spawnStats, ammo, slot.Itemstack, shooter);

            count++;
        }

        Inventory.Write(slot);
        Inventory.Clear();

        slot.Itemstack.Item.DamageItem(player.Entity.World, player.Entity, slot, 1 + additionalDurabilityCost);
        SetLoadStage(slot, stage);
        SetReadyState(slot, state);
        slot.MarkDirty();

        return true;
    }

    protected readonly RevolverStats Stats;
    protected readonly ItemInventoryBuffer Inventory = new();
    protected const string InventoryId = "magazine";
    protected const string LoadingStageAttribute = "CombatOverhaul:loading-stage";
    protected const string WeaponReadyStageAttribute = "CombatOverhaul:ready-stage";

    protected void SetLoadStage(ItemSlot slot, int stage)
    {
        slot.Itemstack?.Attributes.SetInt(LoadingStageAttribute, stage);
    }
    protected void SetReadyState(ItemSlot slot, RevolverReadyState state)
    {
        slot.Itemstack?.Attributes.SetInt(WeaponReadyStageAttribute, (int)state);
    }

    protected ItemSlot? GetFlask(IServerPlayer player, int powderNeeded)
    {
        ItemSlot? flaskSlot = null;
        player.Entity.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Item?.Code == null) return true;

            if (
                WildcardUtil.Match(Stats.FlaskWildcard, slot.Itemstack.Item.Code.ToString()) &&
                slot.Itemstack.Item.GetRemainingDurability(slot.Itemstack) >= powderNeeded)
            {
                flaskSlot = slot;
                return false;
            }

            return true;
        });
        return flaskSlot;
    }
    protected ItemSlot? GetWadding(IServerPlayer player, int amount)
    {
        ItemSlot? waddingSlot = null;
        player.Entity.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Item?.Code == null) return true;

            if (
                WildcardUtil.Match(Stats.WaddingWildcard, slot.Itemstack.Item.Code.ToString()) &&
                slot.Itemstack.StackSize >= amount)
            {
                waddingSlot = slot;
                return false;
            }

            return true;
        });
        return waddingSlot;
    }
}

public class RevolverItem : Item, IHasWeaponLogic, IHasRangedWeaponLogic, IHasDynamicIdleAnimations
{
    public RevolverClient? ClientLogic { get; private set; }
    public RevolverServer? ServerLogic { get; private set; }

    public AnimationRequestByCode IdleAnimation { get; private set; }
    public AnimationRequestByCode ReadyAnimation { get; private set; }
    public AnimationRequestByCode IdleAnimationOffhand { get; private set; }
    public AnimationRequestByCode ReadyAnimationOffhand { get; private set; }

    public RevolverStats? Stats { get; private set; }

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

    public AnimationRequestByCode? GetIdleAnimation(EntityPlayer player, ItemSlot slot, bool mainHand) => mainHand ? IdleAnimation : IdleAnimationOffhand;
    public AnimationRequestByCode? GetReadyAnimation(EntityPlayer player, ItemSlot slot, bool mainHand) => mainHand ? ReadyAnimation : ReadyAnimationOffhand;

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        if (Stats != null && Stats.ProficiencyStat != "")
        {
            string description = Lang.Get("combatoverhaul:iteminfo-proficiency", Lang.Get($"combatoverhaul:proficiency-{Stats.ProficiencyStat}"));
            dsc.AppendLine(description);
        }

        if (Stats != null)
        {
            dsc.AppendLine(Lang.Get("combatoverhaul:iteminfo-range-weapon-damage", Stats.FiringStats.BulletDamageMultiplier, Stats.FiringStats.BulletDamageTier));
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
            Stats = Attributes.AsObject<RevolverStats>();
            IdleAnimation = new(Stats.IdleAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            ReadyAnimation = new(Stats.ReadyAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            //IdleAnimationOffhand = new(Stats.IdleAnimationOffhand, 1, 1, "mainOffhand", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            //ReadyAnimationOffhand = new(Stats.ReadyAnimationOffhand, 1, 1, "mainOffhand", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
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
