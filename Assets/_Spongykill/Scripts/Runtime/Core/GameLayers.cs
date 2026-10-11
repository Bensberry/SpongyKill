using UnityEngine;

namespace Spongykill.Core
{
    /// <summary>
    /// Physics layer names used by the game. The editor setup tool creates them in slots 8-11.
    /// </summary>
    public static class GameLayers
    {
        public const string Player = "Player";
        public const string Bot = "Bot";
        public const string Hitbox = "Hitbox";
        public const string Viewmodel = "Viewmodel";

        /// <summary>Default raycast layers minus the given layer names (names that don't exist are skipped).</summary>
        public static int DefaultMaskExcluding(params string[] layerNames)
        {
            int mask = Physics.DefaultRaycastLayers;
            foreach (string layerName in layerNames)
            {
                int layer = LayerMask.NameToLayer(layerName);
                if (layer >= 0) mask &= ~(1 << layer);
            }
            return mask;
        }
    }
}
