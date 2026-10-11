using UnityEngine;
using UnityEngine.Rendering;

namespace Spongykill.Weapons
{
    /// <summary>Puts a weapon's model in a character's hands for others to see (bots and remote players).</summary>
    public static class ThirdPersonWeapon
    {
        /// <returns>The model's viewmodel component (used only for its muzzle and flash), or null.</returns>
        public static WeaponViewmodel Attach(Transform mount, WeaponDefinition weapon, ref GameObject model)
        {
            if (model) Object.Destroy(model);
            model = null;
            if (weapon == null || weapon.viewmodelPrefab == null || mount == null) return null;

            model = Object.Instantiate(weapon.viewmodelPrefab, mount, false);
            model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            int layer = mount.gameObject.layer;
            foreach (Transform part in model.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = layer;
            foreach (Renderer part in model.GetComponentsInChildren<Renderer>(true)) part.shadowCastingMode = ShadowCastingMode.On;
            return model.GetComponent<WeaponViewmodel>();
        }
    }
}
