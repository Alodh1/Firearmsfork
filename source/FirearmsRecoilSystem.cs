using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace Firearms;

internal static class FirearmsRecoilSystem
{
    private const string HarmonyId = "firearmsfork.recoil";
    private const float ReturnStrength = 13f;
    private const float Damping = 8f;
    private const float MinPitchOffset = -18f * GameMath.DEG2RAD;
    private const float MaxPitchOffset = 3f * GameMath.DEG2RAD;
    private const float MinYawOffset = -3f * GameMath.DEG2RAD;
    private const float MaxYawOffset = 3f * GameMath.DEG2RAD;
    private const float MinCameraPitch = 1.5857964f;
    private const float MaxCameraPitch = 4.697389f;
    private static readonly Random Random = new();

    private static Harmony? _harmony;
    private static ICoreClientAPI? _api;
    private static float _recoilPitchOffset;
    private static float _recoilYawOffset;
    private static float _appliedPitchOffset;
    private static float _appliedYawOffset;
    private static float _recoilPitchVelocity;
    private static float _recoilYawVelocity;
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
        _recoilPitchOffset = 0;
        _recoilYawOffset = 0;
        _appliedPitchOffset = 0;
        _appliedYawOffset = 0;
        _recoilPitchVelocity = 0;
        _recoilYawVelocity = 0;
    }

    public static void AddRecoil(float verticalDeg, float horizontalDeg)
    {
        if (_api == null) return;

        long now = _api.World.ElapsedMilliseconds;
        if (now - _lastRecoilMs < 40) return;
        _lastRecoilMs = now;

        float vertical = GameMath.Clamp(verticalDeg, 0f, 12f) * GameMath.DEG2RAD;
        float horizontal = GameMath.Clamp(horizontalDeg, 0f, 1.8f) * GameMath.DEG2RAD;

        // In Vintage Story's pitch convention, lower pitch raises the camera.
        float yawImpulse = ((float)Random.NextDouble() * 2f - 1f) * horizontal;
        _recoilPitchOffset = GameMath.Clamp(_recoilPitchOffset - vertical, MinPitchOffset, MaxPitchOffset);
        _recoilYawOffset = GameMath.Clamp(_recoilYawOffset + yawImpulse, MinYawOffset, MaxYawOffset);

        // A small velocity tail keeps the motion from looking like a hard snap, but the visible impulse is the offset above.
        _recoilPitchVelocity -= vertical * 2f;
        _recoilYawVelocity += yawImpulse * 2f;
    }

    private static void UpdateCameraYawPitchPostfix(ClientMain __instance, float dt, ref float ___mousePitch, ref float ___mouseYaw)
    {
        if (_api == null || __instance.EntityPlayer?.Pos == null) return;

        dt = GameMath.Clamp(dt, 0f, 0.1f);
        if (dt <= 0) return;

        _recoilPitchVelocity -= _recoilPitchOffset * ReturnStrength * dt;
        _recoilPitchVelocity -= _recoilPitchVelocity * Damping * dt;
        _recoilYawVelocity -= _recoilYawOffset * ReturnStrength * dt;
        _recoilYawVelocity -= _recoilYawVelocity * Damping * dt;

        _recoilPitchOffset += _recoilPitchVelocity * dt;
        _recoilYawOffset += _recoilYawVelocity * dt;

        _recoilPitchOffset = GameMath.Clamp(_recoilPitchOffset, MinPitchOffset, MaxPitchOffset);
        _recoilYawOffset = GameMath.Clamp(_recoilYawOffset, MinYawOffset, MaxYawOffset);

        float pitchDelta = _recoilPitchOffset - _appliedPitchOffset;
        float yawDelta = _recoilYawOffset - _appliedYawOffset;

        if (Math.Abs(pitchDelta) < 0.000001f && Math.Abs(yawDelta) < 0.000001f) return;

        __instance.EntityPlayer.Pos.Pitch = GameMath.Clamp(__instance.EntityPlayer.Pos.Pitch + pitchDelta, MinCameraPitch, MaxCameraPitch);
        __instance.EntityPlayer.Pos.Yaw = GameMath.Mod(__instance.EntityPlayer.Pos.Yaw + yawDelta, (float)Math.PI * 2f);

        ___mousePitch = __instance.EntityPlayer.Pos.Pitch;
        ___mouseYaw = __instance.EntityPlayer.Pos.Yaw;
        _appliedPitchOffset = _recoilPitchOffset;
        _appliedYawOffset = _recoilYawOffset;
    }
}
