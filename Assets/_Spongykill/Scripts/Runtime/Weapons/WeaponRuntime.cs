using UnityEngine;

namespace Spongykill.Weapons
{
    /// <summary>One owner's copy of a weapon: ammunition, fire cooldown and accumulated bloom.</summary>
    public class WeaponRuntime
    {
        public WeaponRuntime(WeaponDefinition definition)
        {
            Definition = definition;
            Refill();
        }

        public WeaponDefinition Definition { get; }
        public int Magazine { get; private set; }
        public int Reserve { get; private set; }
        public float Bloom { get; private set; }
        public float NextFireTime { get; set; }
        /// <summary>First-person model, if the owner shows one.</summary>
        public WeaponViewmodel Viewmodel { get; set; }

        public bool IsMelee => Definition.IsMelee;
        public bool HasAmmo => IsMelee || Magazine > 0;
        public bool CanReload => !IsMelee && Magazine < Definition.magazineSize && Reserve > 0;

        public void Refill()
        {
            Magazine = Definition.magazineSize;
            Reserve = Definition.reserveAmmo;
            Bloom = 0f;
        }

        public bool TryConsumeRound()
        {
            if (IsMelee) return true;
            if (Magazine <= 0) return false;
            Magazine--;
            return true;
        }

        public void FinishReload()
        {
            int loaded = Mathf.Min(Definition.magazineSize - Magazine, Reserve);
            Magazine += loaded;
            Reserve -= loaded;
        }

        public void AddBloom(float amount) => Bloom = Mathf.Min(Bloom + amount, Definition.maxBloom);

        public void RecoverBloom(float amount) => Bloom = Mathf.Max(0f, Bloom - amount);
    }
}
