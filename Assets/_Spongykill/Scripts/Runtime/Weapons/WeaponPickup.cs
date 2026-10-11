using UnityEngine;

namespace Spongykill.Weapons
{
    /// <summary>
    /// A weapon lying in the world that the player can take with the interact key. Shows a spinning
    /// copy of the weapon's model. Racks in the test range never run out.
    /// </summary>
    public class WeaponPickup : MonoBehaviour
    {
        [SerializeField] WeaponDefinition weapon;
        [Tooltip("Racks keep offering the weapon; dropped weapons disappear once taken.")]
        [SerializeField] bool infinite = true;
        [SerializeField] Transform displayRoot;
        [SerializeField] float spinSpeed = 60f;

        /// <summary>Viewmodel parts with this name prefix are the player's hands and are not shown on pickups.</summary>
        public const string GloveNamePrefix = "Glove";

        public WeaponDefinition Weapon => weapon;

        public void Configure(WeaponDefinition newWeapon, bool isInfinite)
        {
            weapon = newWeapon;
            infinite = isInfinite;
        }

        void Start()
        {
            if (weapon == null || weapon.viewmodelPrefab == null) return;
            Transform parent = displayRoot ? displayRoot : transform;
            GameObject display = Instantiate(weapon.viewmodelPrefab, parent, false);
            display.transform.localPosition = Vector3.zero;
            // The prefab is a first-person model: drop its animation script and move it to the world layers.
            if (display.TryGetComponent(out WeaponViewmodel viewmodel)) Destroy(viewmodel);
            foreach (Transform child in display.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.layer = gameObject.layer;
                if (child.name.StartsWith(GloveNamePrefix)) Destroy(child.gameObject);
            }
            foreach (Light light in display.GetComponentsInChildren<Light>(true)) light.enabled = false;
        }

        void Update()
        {
            if (displayRoot) displayRoot.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
        }

        public void Collect()
        {
            if (!infinite) Destroy(gameObject);
        }
    }
}
