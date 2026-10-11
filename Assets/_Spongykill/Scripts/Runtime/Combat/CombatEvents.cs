using System;
using UnityEngine;

namespace Spongykill.Combat
{
    /// <summary>One elimination, for the kill feed and (later) scoring.</summary>
    public readonly struct EliminationRecord
    {
        /// <summary>Who dealt the final blow; null for hazards, falls and the like.</summary>
        public readonly TeamMember Killer;
        public readonly TeamMember Victim;
        public readonly string Source;
        public readonly bool Headshot;

        public EliminationRecord(TeamMember killer, TeamMember victim, string source, bool headshot)
        {
            Killer = killer;
            Victim = victim;
            Source = source;
            Headshot = headshot;
        }
    }

    /// <summary>Scene-wide combat notifications: eliminations and noises bots can hear.</summary>
    public static class CombatEvents
    {
        public static event Action<EliminationRecord> Eliminated;
        /// <summary>A sound at a position, audible within a radius (metres), made by the given root object.</summary>
        public static event Action<Vector3, float, GameObject> Noise;
        /// <summary>Someone fired (seen on every machine, remote shooters included), for the minimap.</summary>
        public static event Action<GameObject> ShotFired;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Eliminated = null;
            Noise = null;
            ShotFired = null;
        }

        public static void RaiseElimination(in EliminationRecord record) => Eliminated?.Invoke(record);

        public static void MakeNoise(Vector3 position, float radius, GameObject source) => Noise?.Invoke(position, radius, source);

        public static void RaiseShot(GameObject shooter) => ShotFired?.Invoke(shooter);
    }
}
