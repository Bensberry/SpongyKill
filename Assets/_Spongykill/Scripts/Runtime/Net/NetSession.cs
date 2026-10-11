using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Spongykill.Combat;
using Spongykill.Core;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Spongykill.Net
{
    public enum SessionMode
    {
        None,
        /// <summary>Single player: a local host only this machine can join.</summary>
        VsBots,
        /// <summary>A host other machines on the network can join.</summary>
        LanHost,
        Client,
        /// <summary>Headless server (Edgegap or a local test), started from the command line.</summary>
        DedicatedServer,
    }

    /// <summary>
    /// Starts and ends network sessions: playing against bots (a private host), hosting on the LAN,
    /// joining an address, or running headless as a dedicated server. Lives on the NetworkManager in
    /// the main menu and survives scene loads with it. Connection approval checks the game version and
    /// the player limit and records each player's name.
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class NetSession : MonoBehaviour
    {
        public const string MenuScene = "MainMenu";
        public const string GameScene = "CoralPlaza";
        public const ushort DefaultPort = 7777;
        /// <summary>Bump when the network protocol changes so old clients are told to update.</summary>
        public const int ProtocolVersion = 1;

        const string NamePrefsKey = "spongykill.playername";
        const string DifficultyPrefsKey = "spongykill.botdifficulty";

        [Tooltip("Players and bots. Registered at startup so every build knows the same prefabs.")]
        [SerializeField] NetworkPrefabsList networkPrefabs;
        [SerializeField, Range(2, 16)] int maxPlayers = 10;
        [SerializeField] float connectTimeout = 15f;
        [Tooltip("A dedicated server shuts down after this long with nobody connected.")]
        [SerializeField] float idleShutdownSeconds = 300f;

        [Serializable]
        class ConnectionPayload
        {
            public int version;
            public string name;
            public string ticket;
        }

        static readonly Dictionary<ulong, string> Names = new Dictionary<ulong, string>();

        NetworkManager manager;
        UnityTransport transport;
        float connectStarted = -1f;
        float idleTime;
        bool leaving;

        public static NetSession Instance { get; private set; }
        public int MaxPlayers => maxPlayers;
        public static SessionMode Mode { get; private set; }
        /// <summary>Why the last session ended, for the main menu to show. Cleared once shown.</summary>
        public static string LastDisconnectReason { get; set; }
        /// <summary>Matchmaking ticket to present to the server when joining through Quick Match.</summary>
        public static string PendingTicket { get; set; }

        /// <summary>A client was approved (server side), with the matchmaking ticket it presented.</summary>
        public static event Action<ulong, string> ClientApproved;
        /// <summary>A client left (server side).</summary>
        public static event Action<ulong> ClientLeft;
        /// <summary>The dedicated server is about to quit after sitting empty; release hosting resources.</summary>
        public static event Action<Action> IdleShutdown;

        public static string LocalName
        {
            get
            {
                string saved = PlayerPrefs.GetString(NamePrefsKey, "");
                if (!string.IsNullOrWhiteSpace(saved)) return saved;
                // First run: pick a name once and keep it.
                saved = "Sponge" + UnityEngine.Random.Range(100, 999);
                PlayerPrefs.SetString(NamePrefsKey, saved);
                PlayerPrefs.Save();
                return saved;
            }
            set
            {
                PlayerPrefs.SetString(NamePrefsKey, Clean(value));
                PlayerPrefs.Save();
            }
        }

        /// <summary>0 = Easy, 1 = Normal, 2 = Hard.</summary>
        public static int BotDifficultyLevel
        {
            get => PlayerPrefs.GetInt(DifficultyPrefsKey, 1);
            set
            {
                PlayerPrefs.SetInt(DifficultyPrefsKey, Mathf.Clamp(value, 0, 2));
                PlayerPrefs.Save();
            }
        }

        public static bool IsDedicatedServer => Mode == SessionMode.DedicatedServer;

        public bool IsConnecting => connectStarted >= 0f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            Mode = SessionMode.None;
            LastDisconnectReason = null;
            PendingTicket = null;
            ClientApproved = null;
            ClientLeft = null;
            IdleShutdown = null;
            Names.Clear();
        }

        public static string NameOf(ulong clientId) => Names.TryGetValue(clientId, out string playerName) ? playerName : "Player";

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            manager = GetComponent<NetworkManager>();
            transport = GetComponent<UnityTransport>();
            manager.NetworkConfig.ConnectionApproval = true;
            manager.ConnectionApprovalCallback = Approve;
            manager.OnClientConnectedCallback += OnClientConnected;
            manager.OnClientDisconnectCallback += OnClientDisconnected;
            // Registered here rather than trusting the NetworkManager's own list, which the editor tooling can reset.
            List<NetworkPrefabsList> lists = manager.NetworkConfig.Prefabs.NetworkPrefabsLists;
            if (networkPrefabs != null && !lists.Contains(networkPrefabs)) lists.Add(networkPrefabs);
            if (lists.Count == 0)
                Debug.LogError("[Spongykill] No network prefab list; clients won't be able to spawn players or bots. Rebuild the main menu.");
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            if (manager == null) return;
            manager.ConnectionApprovalCallback = null;
            manager.OnClientConnectedCallback -= OnClientConnected;
            manager.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        void Start()
        {
            if (LaunchedAsServer(out ushort port)) StartDedicatedServer(port);
        }

        void Update()
        {
            if (IsConnecting && Time.unscaledTime - connectStarted > connectTimeout && !manager.IsConnectedClient)
                Leave("Couldn't reach the server.");

            if (Mode != SessionMode.DedicatedServer || !manager.IsServer) return;
            idleTime = manager.ConnectedClientsIds.Count == 0 ? idleTime + Time.unscaledDeltaTime : 0f;
            if (idleTime > idleShutdownSeconds && !leaving) ShutdownIdleServer();
        }

        // ------------------------------------------------------------------ Starting sessions

        /// <summary>Single player against bots: a private host on this machine.</summary>
        public bool StartVsBots(int difficulty)
        {
            BotDifficultyLevel = difficulty;
            return StartHost(SessionMode.VsBots, "127.0.0.1");
        }

        /// <summary>Host a game that other machines on the network can join on <see cref="DefaultPort"/>.</summary>
        public bool StartLanHost() => StartHost(SessionMode.LanHost, "0.0.0.0");

        bool StartHost(SessionMode mode, string listenAddress)
        {
            if (manager.IsListening) return false;
            Mode = mode;
            CombatAuthority.IsAuthoritative = true;
            SetPayload(null);
            // A local dedicated server may already use the default port; fall back to a free one.
            ushort port = mode == SessionMode.VsBots ? FreePort() : DefaultPort;
            transport.SetConnectionData(listenAddress == "0.0.0.0" ? "127.0.0.1" : listenAddress, port, listenAddress);
            if (!manager.StartHost())
            {
                Mode = SessionMode.None;
                LastDisconnectReason = "Couldn't start the game (is the port in use?).";
                return false;
            }
            manager.SceneManager.LoadScene(GameScene, LoadSceneMode.Single);
            return true;
        }

        /// <summary>Join a server by IP address or host name.</summary>
        public bool Join(string address, ushort port, string ticket = null)
        {
            if (manager.IsListening) return false;
            if (!TryResolve(address, out string ip))
            {
                LastDisconnectReason = $"Couldn't find the server \"{address}\".";
                return false;
            }
            Mode = SessionMode.Client;
            CombatAuthority.IsAuthoritative = false;
            SetPayload(ticket);
            transport.SetConnectionData(ip, port);
            if (!manager.StartClient())
            {
                Mode = SessionMode.None;
                CombatAuthority.IsAuthoritative = true;
                LastDisconnectReason = "Couldn't start the connection.";
                return false;
            }
            connectStarted = Time.unscaledTime;
            return true;
        }

        public void StartDedicatedServer(ushort port)
        {
            Mode = SessionMode.DedicatedServer;
            CombatAuthority.IsAuthoritative = true;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            transport.SetConnectionData("0.0.0.0", port, "0.0.0.0");
            if (!manager.StartServer())
            {
                Debug.LogError($"[Spongykill] Dedicated server failed to listen on UDP port {port}.");
                Application.Quit(1);
                return;
            }
            Debug.Log($"[Spongykill] Dedicated server listening on UDP port {port} (protocol v{ProtocolVersion}).");
            manager.SceneManager.LoadScene(GameScene, LoadSceneMode.Single);
        }

        /// <summary>End the session (host, client or server) and go back to the main menu.</summary>
        public void Leave(string reason = null)
        {
            if (leaving) return;
            leaving = true;
            LastDisconnectReason = reason;
            StartCoroutine(ShutdownAndReturn());
        }

        IEnumerator ShutdownAndReturn()
        {
            connectStarted = -1f;
            if (manager.IsListening) manager.Shutdown();
            while (manager.ShutdownInProgress) yield return null;

            Mode = SessionMode.None;
            PendingTicket = null;
            Names.Clear();
            CombatAuthority.IsAuthoritative = true;
            CombatAuthority.ForwardDamage = null;
            InputGate.MenuOpen = false;
            InputGate.MatchFrozen = false;
            Time.timeScale = 1f;
            CursorLock.Unlock();
            // The menu scene brings a fresh NetworkManager; this one must go first.
            Destroy(gameObject);
            SceneManager.LoadScene(MenuScene);
        }

        void ShutdownIdleServer()
        {
            leaving = true;
            Debug.Log($"[Spongykill] No players for {idleShutdownSeconds:0} s; shutting down.");
            void Quit()
            {
                manager.Shutdown();
                Application.Quit();
            }
            if (IdleShutdown != null) IdleShutdown.Invoke(Quit);
            else Quit();
        }

        // ------------------------------------------------------------------ Connection approval

        void SetPayload(string ticket)
        {
            var payload = new ConnectionPayload { version = ProtocolVersion, name = LocalName, ticket = ticket ?? "" };
            manager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
        }

        void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            ConnectionPayload payload = null;
            try
            {
                if (request.Payload != null && request.Payload.Length > 0 && request.Payload.Length < 1024)
                    payload = JsonUtility.FromJson<ConnectionPayload>(Encoding.UTF8.GetString(request.Payload));
            }
            catch (ArgumentException)
            {
                payload = null;
            }

            response.CreatePlayerObject = false;
            if (payload == null || payload.version != ProtocolVersion)
            {
                response.Approved = false;
                response.Reason = "This server runs a different version of Spongykill Ops. Please update.";
                return;
            }
            if (manager.ConnectedClientsIds.Count >= maxPlayers)
            {
                response.Approved = false;
                response.Reason = "The server is full.";
                return;
            }

            Names[request.ClientNetworkId] = UniqueName(Clean(payload.name));
            response.Approved = true;
            ClientApproved?.Invoke(request.ClientNetworkId, payload.ticket);
        }

        void OnClientConnected(ulong clientId)
        {
            if (clientId == manager.LocalClientId) connectStarted = -1f;
        }

        void OnClientDisconnected(ulong clientId)
        {
            if (manager.IsServer)
            {
                Names.Remove(clientId);
                ClientLeft?.Invoke(clientId);
                return;
            }
            // This client was dropped or refused.
            string reason = string.IsNullOrEmpty(manager.DisconnectReason) ? "Disconnected from the server." : manager.DisconnectReason;
            Leave(reason);
        }

        // ------------------------------------------------------------------ Helpers

        static string Clean(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "Player";
            var builder = new StringBuilder();
            foreach (char c in text.Trim())
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '_' || c == '-') builder.Append(c);
            string cleaned = builder.ToString().Trim();
            if (cleaned.Length > 16) cleaned = cleaned.Substring(0, 16);
            return cleaned.Length == 0 ? "Player" : cleaned;
        }

        static string UniqueName(string wanted)
        {
            string candidate = wanted;
            for (int suffix = 2; Names.ContainsValue(candidate); suffix++) candidate = $"{wanted} {suffix}";
            return candidate;
        }

        static bool TryResolve(string address, out string ip)
        {
            ip = null;
            if (string.IsNullOrWhiteSpace(address)) return false;
            address = address.Trim();
            if (IPAddress.TryParse(address, out IPAddress parsed))
            {
                ip = parsed.ToString();
                return true;
            }
            try
            {
                foreach (IPAddress candidate in Dns.GetHostAddresses(address))
                {
                    if (candidate.AddressFamily != AddressFamily.InterNetwork) continue;
                    ip = candidate.ToString();
                    return true;
                }
            }
            catch (SocketException)
            {
            }
            return false;
        }

        static ushort FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();
                return (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
            }
            catch (SocketException)
            {
                return DefaultPort + 1;
            }
            finally
            {
                listener.Stop();
            }
        }

        /// <summary>Dedicated server builds, or any build started with -server [-port N].</summary>
        static bool LaunchedAsServer(out ushort port)
        {
            port = DefaultPort;
            string[] args = Environment.GetCommandLineArgs();
            bool server = false;
#if UNITY_SERVER
            server = true;
#endif
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-server") server = true;
                if (args[i] == "-port" && i + 1 < args.Length && ushort.TryParse(args[i + 1], out ushort parsed)) port = parsed;
            }
            return server && !Application.isEditor;
        }
    }
}
