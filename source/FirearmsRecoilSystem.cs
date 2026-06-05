using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace Firearms;

public class RecoilStats
{
    public bool Enabled { get; set; } = true;
    public float Strength { get; set; } = 1f;
    public float VerticalBaseDeg { get; set; } = 2.5f;
    public float VerticalDamageScaleDeg { get; set; } = 0.45f;
    public float VerticalMinDeg { get; set; } = 4f;
    public float VerticalMaxDeg { get; set; } = 12f;
    public float HorizontalBaseDeg { get; set; } = 0f;
    public float HorizontalVerticalScale { get; set; } = 0.15f;
    public float HorizontalDamageScaleDeg { get; set; } = 0f;
    public float HorizontalMinDeg { get; set; } = 0.35f;
    public float HorizontalMaxDeg { get; set; } = 1.8f;
    public float RiseDurationSec { get; set; } = 0.18f;
    public bool SpringBack { get; set; } = true;
    public float SpringBackFraction { get; set; } = 0.28f;
    public float SpringBackDelaySec { get; set; } = 0.04f;
    public float SpringBackDurationSec { get; set; } = 0.55f;
    public float MaxQueuedVerticalDeg { get; set; } = 18f;
    public float MaxQueuedHorizontalDeg { get; set; } = 3f;
    public int MinIntervalMs { get; set; } = 80;
    public float CameraShakeBase { get; set; } = 0.0018f;
    public float CameraShakeDamageScale { get; set; } = 0.00004f;
    public float CameraShakeMin { get; set; } = 0.0018f;
    public float CameraShakeMax { get; set; } = 0.004f;

    public static RecoilStats MuzzleloaderDefaults() => new();

    public static RecoilStats RevolverDefaults() => new()
    {
        VerticalBaseDeg = 2f,
        VerticalDamageScaleDeg = 0.35f,
        VerticalMinDeg = 3f,
        VerticalMaxDeg = 10f,
        HorizontalMinDeg = 0.3f,
        HorizontalMaxDeg = 1.5f,
        CameraShakeBase = 0.0016f,
        CameraShakeDamageScale = 0.000035f,
        CameraShakeMin = 0.0016f,
        CameraShakeMax = 0.0035f
    };

    public float VerticalDegrees(float estimatedDamage)
    {
        float vertical = VerticalBaseDeg + estimatedDamage * VerticalDamageScaleDeg;
        vertical = GameMath.Clamp(vertical, Math.Min(VerticalMinDeg, VerticalMaxDeg), Math.Max(VerticalMinDeg, VerticalMaxDeg));
        return Math.Max(0f, vertical * Strength);
    }

    public float HorizontalDegrees(float estimatedDamage, float verticalDeg)
    {
        float horizontal = HorizontalBaseDeg + verticalDeg * HorizontalVerticalScale + estimatedDamage * HorizontalDamageScaleDeg;
        horizontal = GameMath.Clamp(horizontal, Math.Min(HorizontalMinDeg, HorizontalMaxDeg), Math.Max(HorizontalMinDeg, HorizontalMaxDeg));
        return Math.Max(0f, horizontal * Strength);
    }

    public float CameraShake(float estimatedDamage)
    {
        float shake = CameraShakeBase + estimatedDamage * CameraShakeDamageScale;
        return GameMath.Clamp(shake, Math.Min(CameraShakeMin, CameraShakeMax), Math.Max(CameraShakeMin, CameraShakeMax));
    }
}

internal static class FirearmsRecoilSystem
{
    private const string HarmonyId = "firearmsfork.recoil";
    private const float MinCameraPitch = 1.5857964f;
    private const float MaxCameraPitch = 4.697389f;
    private static readonly Random Random = new();

    private static Harmony? _harmony;
    private static ICoreClientAPI? _api;
    private static float _pendingPitchKick;
    private static float _pendingYawKick;
    private static float _pendingPitchRecovery;
    private static float _pendingYawRecovery;
    private static float _recoveryDelayRemaining;
    private static float _riseDuration = 0.18f;
    private static float _recoveryDuration = 0.55f;
    private static float _maxPendingPitchKick = 18f * GameMath.DEG2RAD;
    private static float _maxPendingYawKick = 3f * GameMath.DEG2RAD;
    private static long _lastRecoilMs = -1000;

    public static void Start(ICoreClientAPI api)
    {
        _api = api;
        _harmony ??= new Harmony(HarmonyId);

        var method = AccessTools.Method(typeof(ClientMain), nameof(ClientMain.UpdateCameraYawPitch), [typeof(float)]);
        _harmony.Patch(method, postfix: new HarmonyMethod(typeof(FirearmsRecoilSystem), nameof(UpdateCameraYawPitchPostfix)));
    }

    public static void Stop()
    {
        _harmony?.UnpatchAll(HarmonyId);
        _harmony = null;
        _api = null;
        _pendingPitchKick = 0;
        _pendingYawKick = 0;
        _pendingPitchRecovery = 0;
        _pendingYawRecovery = 0;
        _recoveryDelayRemaining = 0;
        _riseDuration = 0.18f;
        _recoveryDuration = 0.55f;
        _maxPendingPitchKick = 18f * GameMath.DEG2RAD;
        _maxPendingYawKick = 3f * GameMath.DEG2RAD;
    }

    public static void AddRecoil(float verticalDeg, float horizontalDeg, RecoilStats recoil)
    {
        if (_api == null || !recoil.Enabled) return;

        long now = _api.World.ElapsedMilliseconds;
        if (now - _lastRecoilMs < Math.Max(0, recoil.MinIntervalMs)) return;
        _lastRecoilMs = now;

        _riseDuration = Math.Max(0.001f, recoil.RiseDurationSec);
        _recoveryDuration = Math.Max(0.001f, recoil.SpringBackDurationSec);
        _maxPendingPitchKick = Math.Max(0.1f, recoil.MaxQueuedVerticalDeg) * GameMath.DEG2RAD;
        _maxPendingYawKick = Math.Max(0.1f, recoil.MaxQueuedHorizontalDeg) * GameMath.DEG2RAD;

        float vertical = Math.Max(0f, verticalDeg) * GameMath.DEG2RAD;
        float horizontal = Math.Max(0f, horizontalDeg) * GameMath.DEG2RAD;

        // In Vintage Story's pitch convention, lower pitch raises the camera.
        float yawImpulse = ((float)Random.NextDouble() * 2f - 1f) * horizontal;
        _pendingPitchKick = GameMath.Clamp(_pendingPitchKick - vertical, -_maxPendingPitchKick, _maxPendingPitchKick);
        _pendingYawKick = GameMath.Clamp(_pendingYawKick + yawImpulse, -_maxPendingYawKick, _maxPendingYawKick);

        if (recoil.SpringBack && recoil.SpringBackFraction > 0f)
        {
            float springBackFraction = Math.Max(0f, recoil.SpringBackFraction);
            _pendingPitchRecovery = GameMath.Clamp(_pendingPitchRecovery + vertical * springBackFraction, -_maxPendingPitchKick, _maxPendingPitchKick);
            _pendingYawRecovery = GameMath.Clamp(_pendingYawRecovery - yawImpulse * springBackFraction, -_maxPendingYawKick, _maxPendingYawKick);
            _recoveryDelayRemaining = Math.Max(_recoveryDelayRemaining, Math.Max(0f, recoil.SpringBackDelaySec));
        }
    }

    private static void UpdateCameraYawPitchPostfix(ClientMain __instance, float dt, ref float ___mousePitch, ref float ___mouseYaw)
    {
        if (_api == null || __instance.EntityPlayer?.Pos == null) return;

        dt = GameMath.Clamp(dt, 0f, 0.1f);
        if (dt <= 0) return;

        float pitchDelta = ConsumeKick(ref _pendingPitchKick, dt, _riseDuration);
        float yawDelta = ConsumeKick(ref _pendingYawKick, dt, _riseDuration);

        if (Math.Abs(_pendingPitchKick) < 0.0005f && Math.Abs(_pendingYawKick) < 0.0005f)
        {
            _recoveryDelayRemaining = Math.Max(0f, _recoveryDelayRemaining - dt);
            if (_recoveryDelayRemaining <= 0f)
            {
                pitchDelta += ConsumeKick(ref _pendingPitchRecovery, dt, _recoveryDuration);
                yawDelta += ConsumeKick(ref _pendingYawRecovery, dt, _recoveryDuration);
            }
        }

        if (Math.Abs(pitchDelta) < 0.000001f && Math.Abs(yawDelta) < 0.000001f) return;

        __instance.EntityPlayer.Pos.Pitch = GameMath.Clamp(__instance.EntityPlayer.Pos.Pitch + pitchDelta, MinCameraPitch, MaxCameraPitch);
        __instance.EntityPlayer.Pos.Yaw = GameMath.Mod(__instance.EntityPlayer.Pos.Yaw + yawDelta, (float)Math.PI * 2f);

        ___mousePitch = __instance.EntityPlayer.Pos.Pitch;
        ___mouseYaw = __instance.EntityPlayer.Pos.Yaw;
    }

    private static float ConsumeKick(ref float pendingKick, float dt, float duration)
    {
        float absPending = Math.Abs(pendingKick);
        if (absPending < 0.000001f) return 0f;

        // Apply the kick over a short rise window instead of snapping the full recoil in one frame.
        float step = Math.Min(absPending, absPending * dt / duration);
        float delta = MathF.CopySign(step, pendingKick);
        pendingKick -= delta;
        return delta;
    }
}
