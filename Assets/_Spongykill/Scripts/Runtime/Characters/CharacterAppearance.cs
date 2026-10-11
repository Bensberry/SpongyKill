using UnityEngine;

namespace Spongykill.Characters
{
    /// <summary>Knows which body parts take which skin slot and swaps materials when a skin is applied.</summary>
    public class CharacterAppearance : MonoBehaviour
    {
        [SerializeField] Renderer[] skinRenderers = new Renderer[0];
        [SerializeField] Renderer[] outfitRenderers = new Renderer[0];
        [SerializeField] Renderer[] accentRenderers = new Renderer[0];

        public CharacterSkin Current { get; private set; }

        public void Apply(CharacterSkin skin)
        {
            if (skin == null) return;
            Current = skin;
            Assign(skinRenderers, skin.skin);
            Assign(outfitRenderers, skin.outfit);
            Assign(accentRenderers, skin.accent);
        }

        static void Assign(Renderer[] renderers, Material material)
        {
            if (material == null) return;
            foreach (Renderer part in renderers)
                if (part) part.sharedMaterial = material;
        }
    }
}
