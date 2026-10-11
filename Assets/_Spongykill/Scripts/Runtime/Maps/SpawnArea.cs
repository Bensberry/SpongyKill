using System.Collections.Generic;
using Spongykill.Core;
using UnityEngine;

namespace Spongykill.Maps
{
    /// <summary>A team's spawn zone; each child transform is one spawn point (position + facing).</summary>
    public class SpawnArea : MonoBehaviour
    {
        [SerializeField] Team team;

        static readonly List<SpawnArea> Areas = new List<SpawnArea>();

        readonly List<Transform> points = new List<Transform>();

        public static IReadOnlyList<SpawnArea> All => Areas;
        public Team Team => team;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Areas.Clear();

        void OnEnable() => Areas.Add(this);

        void OnDisable() => Areas.Remove(this);

        public IReadOnlyList<Transform> Points
        {
            get
            {
                points.Clear();
                foreach (Transform child in transform) points.Add(child);
                return points;
            }
        }

        public void Configure(Team newTeam) => team = newTeam;

        void OnDrawGizmos()
        {
            Gizmos.color = team == Team.Attackers ? new Color(1f, 0.6f, 0.2f) : new Color(0.3f, 0.6f, 1f);
            foreach (Transform point in transform)
            {
                Gizmos.DrawWireCube(point.position + Vector3.up * 0.9f, new Vector3(0.7f, 1.8f, 0.7f));
                Gizmos.DrawRay(point.position + Vector3.up * 1.6f, point.forward);
            }
        }
    }
}
