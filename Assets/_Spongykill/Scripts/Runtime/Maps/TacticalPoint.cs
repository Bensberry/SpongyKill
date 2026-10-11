using System.Collections.Generic;
using UnityEngine;

namespace Spongykill.Maps
{
    public enum TacticalRole
    {
        /// <summary>A defensive angle on a site or chokepoint, facing the attackers' approach.</summary>
        DefenderHold,
        /// <summary>A point on a lane that attackers pass through on the way to a site.</summary>
        AttackerApproach,
        /// <summary>A spot inside a site where attackers hold after taking it.</summary>
        AttackerSiteHold,
    }

    /// <summary>A position bots move to and hold. The transform's forward is the direction to watch.</summary>
    public class TacticalPoint : MonoBehaviour
    {
        [SerializeField] TacticalRole role;
        [Tooltip("Which site this point serves: A, B or Mid.")]
        [SerializeField] string site = "A";

        static readonly List<TacticalPoint> Points = new List<TacticalPoint>();

        public static IReadOnlyList<TacticalPoint> All => Points;

        public TacticalRole Role => role;
        public string Site => site;
        public float FacingYaw => transform.eulerAngles.y;
        /// <summary>The bot currently holding this point, so two bots don't stack on one spot.</summary>
        public Object Occupant { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Points.Clear();

        public void Configure(TacticalRole newRole, string newSite)
        {
            role = newRole;
            site = newSite;
        }

        void OnEnable() => Points.Add(this);

        void OnDisable()
        {
            Points.Remove(this);
            Occupant = null;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = role == TacticalRole.DefenderHold ? new Color(0.3f, 0.7f, 1f) : role == TacticalRole.AttackerApproach ? new Color(1f, 0.8f, 0.3f) : new Color(1f, 0.45f, 0.2f);
            Gizmos.DrawSphere(transform.position + Vector3.up * 0.2f, 0.25f);
            Gizmos.DrawRay(transform.position + Vector3.up * 0.2f, transform.forward * 1.5f);
        }
    }
}
