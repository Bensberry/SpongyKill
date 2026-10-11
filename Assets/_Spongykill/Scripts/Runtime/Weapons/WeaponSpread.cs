using UnityEngine;

namespace Spongykill.Weapons
{
    /// <summary>The accuracy model shared by players and bots, so everyone's shots follow the same rules.</summary>
    public static class WeaponSpread
    {
        /// <returns>Cone half-angle in degrees.</returns>
        public static float Compute(WeaponDefinition weapon, float aimBlend, float horizontalSpeed, float walkSpeed, bool grounded, bool crouching, float bloom)
        {
            if (weapon.IsMelee) return 0f;

            float spread = Mathf.Lerp(weapon.hipSpread, weapon.aimSpread, aimBlend);
            // Slow movement (crouch-walking) is forgiving; full speed is not.
            float movement = Mathf.InverseLerp(walkSpeed * 0.35f, walkSpeed, horizontalSpeed);
            spread += weapon.movingSpread * movement + bloom;
            if (!grounded) spread += weapon.airborneSpread;
            if (crouching && grounded) spread *= weapon.crouchSpreadFactor;
            return spread;
        }
    }
}
