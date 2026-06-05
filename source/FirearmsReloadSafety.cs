using CombatOverhaul.Inputs;

namespace Firearms;

internal static class FirearmsReloadSafety
{
    private const float MinAnimationSpeed = 0.05f;
    private const float MaxAnimationSpeed = 10.0f;
    private const float MinWalkspeedPenalty = -0.95f;
    private const float MaxWalkspeedPenalty = 0.0f;

    public static float AnimationSpeed(float speed)
    {
        if (float.IsNaN(speed) || float.IsInfinity(speed) || speed <= 0) return 1.0f;

        return Math.Clamp(speed, MinAnimationSpeed, MaxAnimationSpeed);
    }

    public static void SetReloadWalkspeedPenalty(ActionsManagerPlayerBehavior? playerBehavior, bool mainHand, string mainHandCategory, string offHandCategory, float penalty)
    {
        if (float.IsNaN(penalty) || float.IsInfinity(penalty)) penalty = 0;

        playerBehavior?.SetStat("walkspeed", mainHand ? mainHandCategory : offHandCategory, Math.Clamp(penalty, MinWalkspeedPenalty, MaxWalkspeedPenalty));
    }
}
