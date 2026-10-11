using UnityEngine;

namespace Spongykill.AI
{
    /// <summary>How sharp a bot is. Easy, Normal and Hard presets are generated as assets; tune them in the Inspector.</summary>
    [CreateAssetMenu(menuName = "Spongykill/Bot Difficulty", fileName = "BD_NewDifficulty")]
    public class BotDifficulty : ScriptableObject
    {
        public string displayName = "Normal";

        [Header("Perception")]
        public float viewDistance = 60f;
        [Tooltip("Full width of the vision cone, degrees.")]
        public float viewAngle = 120f;
        [Tooltip("Gunshots within this radius make the bot investigate.")]
        public float hearingRadius = 25f;
        [Tooltip("Seconds between spotting an enemy and the first shot.")]
        public float reactionTime = 0.4f;

        [Header("Aim")]
        [Tooltip("How fast the bot can turn to aim, degrees per second.")]
        public float turnSpeed = 280f;
        [Tooltip("Initial aim error when a target is first spotted, degrees.")]
        public float aimError = 3.5f;
        [Tooltip("Seconds of tracking for the initial aim error to disappear.")]
        public float aimSettleTime = 0.7f;
        [Range(0f, 1f)] public float headshotChance = 0.15f;
        [Tooltip("Multiplies the weapon's normal spread.")]
        public float spreadMultiplier = 1.2f;

        [Header("Trigger discipline")]
        public int burstShots = 4;
        public float burstPause = 0.4f;
        [Tooltip("Extra delay between semi-automatic shots.")]
        public float semiAutoDelay = 0.15f;

        [Header("Tactics")]
        [Range(0f, 1f)] public float strafeChance = 0.4f;
        [Tooltip("Below this fraction of health a bot under fire breaks off to find cover.")]
        [Range(0f, 1f)] public float coverHealthThreshold = 0.4f;
    }
}
