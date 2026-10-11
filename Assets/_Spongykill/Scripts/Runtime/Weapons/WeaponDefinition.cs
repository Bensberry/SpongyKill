using UnityEngine;

namespace Spongykill.Weapons
{
    public enum WeaponSlot
    {
        Primary,
        Secondary,
        Melee,
    }

    public enum WeaponCategory
    {
        Rifle,
        SMG,
        Shotgun,
        Sniper,
        Pistol,
        Melee,
    }

    public enum FireMode
    {
        Automatic,
        SemiAutomatic,
    }

    /// <summary>
    /// Tunable stats for one weapon. Shared by every owner; per-owner state such as ammo lives in
    /// <see cref="WeaponRuntime"/>. Spread values are cone half-angles in degrees.
    /// </summary>
    [CreateAssetMenu(menuName = "Spongykill/Weapon Definition", fileName = "WD_NewWeapon")]
    public class WeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "Weapon";
        public WeaponCategory category = WeaponCategory.Rifle;
        public WeaponSlot slot = WeaponSlot.Primary;
        public GameObject viewmodelPrefab;

        [Header("Damage")]
        public float damage = 30f;
        public float headMultiplier = 4f;
        [Min(1)] public int pellets = 1;
        public float range = 200f;
        [Tooltip("Damage starts dropping off at this distance (m).")]
        public float falloffStart = 30f;
        [Tooltip("Damage reaches its minimum at this distance (m).")]
        public float falloffEnd = 90f;
        [Range(0f, 1f)] public float minDamageFactor = 0.75f;

        [Header("Firing")]
        public FireMode fireMode = FireMode.Automatic;
        public float roundsPerMinute = 600f;
        public int magazineSize = 30;
        public int reserveAmmo = 90;
        public float reloadTime = 2.4f;
        public float drawTime = 0.7f;

        [Header("Accuracy (degrees)")]
        public float hipSpread = 0.55f;
        public float aimSpread = 0.12f;
        [Tooltip("Added at full walking speed.")]
        public float movingSpread = 2.8f;
        public float airborneSpread = 7f;
        [Range(0f, 1f)] public float crouchSpreadFactor = 0.75f;
        [Tooltip("Bloom added by each shot.")]
        public float spreadPerShot = 0.32f;
        public float maxBloom = 3.2f;
        [Tooltip("Bloom removed per second.")]
        public float bloomRecovery = 9f;

        [Header("Recoil (degrees of view kick)")]
        public float recoilVertical = 0.85f;
        public float recoilHorizontal = 0.35f;
        [Tooltip("How fast the view settles back after firing (degrees per second).")]
        public float recoilRecoverySpeed = 12f;
        [Range(0f, 1f)] public float aimRecoilFactor = 0.8f;

        [Header("Aiming")]
        public bool canAim = true;
        public float aimFieldOfView = 52f;
        public float aimTime = 0.2f;
        [Tooltip("Hides the weapon and shows a scope overlay when fully aimed.")]
        public bool aimUsesScope;
        [Range(0.1f, 1f)] public float aimMoveSpeedFactor = 0.65f;

        [Header("Handling")]
        [Range(0.5f, 1.2f)] public float moveSpeedFactor = 1f;

        [Header("Presentation")]
        public Color tracerColor = new Color(1f, 0.95f, 0.6f);
        [Tooltip("Base frequency of the generated shot sound (Hz). 0 = no tonal body, e.g. a melee swoosh.")]
        public float shotSoundPitch = 140f;
        public float shotSoundLength = 0.28f;
        [Range(0f, 1f)] public float shotSoundNoise = 0.6f;

        public bool IsMelee => category == WeaponCategory.Melee;
        public float FireInterval => 60f / Mathf.Max(1f, roundsPerMinute);

        public float DamageAt(float distance)
        {
            float t = Mathf.InverseLerp(falloffStart, falloffEnd, distance);
            return damage * Mathf.Lerp(1f, minDamageFactor, t);
        }
    }
}
