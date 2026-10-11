using Spongykill.Core;
using UnityEngine;

namespace Spongykill.Maps
{
    /// <summary>
    /// Root of a playable map: its name, both teams' spawn areas and the bomb sites. Match, bomb and
    /// bot systems look this up instead of searching the scene for individual pieces.
    /// </summary>
    public class MapDefinition : MonoBehaviour
    {
        [SerializeField] string displayName = "Map";
        [SerializeField] SpawnArea attackerSpawn;
        [SerializeField] SpawnArea defenderSpawn;
        [SerializeField] BombSite[] bombSites = new BombSite[0];

        public string DisplayName => displayName;
        public BombSite[] BombSites => bombSites;

        public SpawnArea SpawnFor(Team team) => team == Team.Attackers ? attackerSpawn : defenderSpawn;

        public void Configure(string newName, SpawnArea attackers, SpawnArea defenders, BombSite[] sites)
        {
            displayName = newName;
            attackerSpawn = attackers;
            defenderSpawn = defenders;
            bombSites = sites;
        }
    }
}
