using CombatOverhaul.RangedSystems;
using CombatOverhaul.Utils;
using Vintagestory.API.Common;

namespace Firearms;

internal static class FirearmsAmmoUtility
{
    public static readonly bool RecoilEnabled = false;
    public static readonly bool ProjectileDisappearanceEnabled = false;

    public static int BulletItemsRequired(int bulletsLoaded, int bulletsLoadedPerBulletItem)
    {
        if (bulletsLoaded <= 0) return 0;

        int loadedPerItem = Math.Max(1, bulletsLoadedPerBulletItem);
        return (bulletsLoaded + loadedPerItem - 1) / loadedPerItem;
    }

    public static bool HasEnoughBulletItems(ItemSlot ammoSlot, int bulletsLoaded, int bulletsLoadedPerBulletItem)
    {
        int bulletItemsRequired = BulletItemsRequired(bulletsLoaded, bulletsLoadedPerBulletItem);
        return ammoSlot.Itemstack?.StackSize >= bulletItemsRequired;
    }

    public static bool TryConsumeAndLoadBullets(ItemInventoryBuffer inventory, ItemSlot ammoSlot, int bulletsLoaded, int bulletsLoadedPerBulletItem)
    {
        if (ammoSlot.Itemstack == null || !HasEnoughBulletItems(ammoSlot, bulletsLoaded, bulletsLoadedPerBulletItem)) return false;

        ItemStack ammoTemplate = ammoSlot.Itemstack.Clone();
        ammoTemplate.StackSize = 1;

        int bulletItemsRequired = BulletItemsRequired(bulletsLoaded, bulletsLoadedPerBulletItem);
        if (bulletItemsRequired > 0) ammoSlot.TakeOut(bulletItemsRequired);

        for (int count = 0; count < bulletsLoaded; count++)
        {
            InventoryItemStackAdd(inventory, ammoTemplate);
        }

        return true;
    }

    public static void ApplyProjectileDisappearance(ItemStack ammo, ProjectileStats stats)
    {
        if (!ProjectileDisappearanceEnabled)
        {
            stats.CanBeCollected = true;
            stats.DropChance = 1;
            return;
        }

        // Firearm projectiles should not be recoverable and should be destroyed
        // once they collide with terrain or an entity.
        stats.CanBeCollected = false;
        stats.DropChance = 0;

    }

    private static void InventoryItemStackAdd(ItemInventoryBuffer inventory, ItemStack ammoTemplate)
    {
        inventory.Items.Add(ammoTemplate.Clone());
    }
}
