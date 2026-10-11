using UnityEngine;

namespace Spongykill.Core
{
    /// <summary>Team names and HUD colours.</summary>
    public static class TeamColors
    {
        public static readonly Color Attackers = new Color(1f, 0.62f, 0.25f);
        public static readonly Color Defenders = new Color(0.4f, 0.9f, 0.95f);

        public static Color For(Team team) => team == Team.Attackers ? Attackers : Defenders;

        public static string NameOf(Team team) => team == Team.Attackers ? "Brine Raiders" : "Reef Guard";
    }
}
