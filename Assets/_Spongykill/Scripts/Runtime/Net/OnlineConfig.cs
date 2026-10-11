using UnityEngine;

namespace Spongykill.Net
{
    /// <summary>
    /// Where online play goes: the Edgegap matchmaker for Quick Match, and defaults for joining by
    /// address. Lives in Resources so the client and the dedicated server read the same values.
    /// The matchmaker auth token ships inside the game, so treat it as public and regenerate it in
    /// the Edgegap dashboard if it is ever abused.
    /// </summary>
    [CreateAssetMenu(menuName = "Spongykill/Online Config", fileName = "OnlineConfig")]
    public class OnlineConfig : ScriptableObject
    {
        public const string ResourcePath = "OnlineConfig";

        [Header("Edgegap matchmaker (Quick Match)")]
        [Tooltip("The matchmaker's API URL from the Edgegap dashboard, e.g. https://xxxx.edgegap.net")]
        public string matchmakerUrl = "";
        [Tooltip("The matchmaker's Auth Token from the Edgegap dashboard.")]
        public string matchmakerToken = "";
        [Tooltip("Profile name in the matchmaker configuration.")]
        public string matchmakerProfile = "spongykill-tdm";
        [Tooltip("\"new\" starts a new server if none has room; \"1\" joins a running match that needs one player.")]
        public string[] backfillGroupSize = { "new", "1" };
        [Tooltip("The game port's name in the Edgegap app version (lower case, as the matchmaker reports it).")]
        public string gamePortName = "gameport";

        [Header("Join by address")]
        public string lastAddress = "127.0.0.1";
        public ushort lastPort = NetSession.DefaultPort;

        static OnlineConfig instance;

        public static OnlineConfig Instance => instance ? instance : instance = Resources.Load<OnlineConfig>(ResourcePath);

        public bool MatchmakingConfigured => !string.IsNullOrWhiteSpace(matchmakerUrl) && !string.IsNullOrWhiteSpace(matchmakerToken);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;
    }
}
