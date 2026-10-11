using UnityEngine;

namespace Spongykill.Core
{
    /// <summary>
    /// Whether gameplay input reaches the player. Menus and the match flow raise blocks here instead of
    /// reaching into the player's components.
    /// </summary>
    public static class InputGate
    {
        /// <summary>A full-screen menu (pause, settings, loadout, results) is open.</summary>
        public static bool MenuOpen { get; set; }

        /// <summary>The match isn't live yet or is over; the player may look around but not act.</summary>
        public static bool MatchFrozen { get; set; }

        /// <summary>A pause menu exists and handles Escape (otherwise Escape just frees the cursor).</summary>
        public static bool MenusPresent { get; set; }

        public static bool GameplayBlocked => MenuOpen || MatchFrozen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            MenuOpen = false;
            MatchFrozen = false;
            MenusPresent = false;
        }
    }
}
