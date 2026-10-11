using System;
using System.Collections.Generic;
using Spongykill.AI;
using Spongykill.Combat;
using Spongykill.Core;
using Spongykill.Maps;
using Spongykill.Net;
using Spongykill.Weapons;
using Unity.Netcode;
using UnityEngine;

namespace Spongykill.Match
{
    public enum MatchPhase
    {
        /// <summary>No human has deployed yet; nothing moves.</summary>
        Waiting,
        /// <summary>Everyone is placed; a short count before the fight starts.</summary>
        Countdown,
        Live,
        /// <summary>Results on screen; the next match starts by itself.</summary>
        Ended,
    }

    /// <summary>One combatant's tally for the scoreboard.</summary>
    public sealed class MemberStats
    {
        public readonly TeamMember Member;
        public readonly int Kills;
        public readonly int Deaths;
        public readonly int Headshots;

        public MemberStats(TeamMember member, int kills, int deaths, int headshots)
        {
            Member = member;
            Kills = kills;
            Deaths = deaths;
            Headshots = headshots;
        }

        public int Score => Kills * 100 + Headshots * 25;
    }

    /// <summary>
    /// Team deathmatch, run by the server (the host when playing against bots, or a dedicated server
    /// online). Humans join the smaller team when they deploy and bots fill the empty slots up to five
    /// a side. The first team to the kill limit, or the leader when time runs out, wins; results show
    /// for a few seconds and the next match starts. Phase, timer and scores are shared with clients.
    /// </summary>
    public class TdmMatch : NetworkBehaviour
    {
        [SerializeField] MapDefinition map;
        [SerializeField] BotSquadSpawner botSpawner;
        [SerializeField] NetworkObject playerPrefab;
        [Tooltip("Shows the map while this machine has no player (choosing a loadout, dedicated server).")]
        [SerializeField] GameObject spectatorCamera;

        [Header("Rules")]
        [SerializeField, Min(1)] int scoreLimit = 50;
        [Tooltip("Match length in seconds.")]
        [SerializeField] float timeLimit = 480f;
        [SerializeField, Range(1, 8)] int teamSize = 5;
        [SerializeField] float countdown = 5f;
        [SerializeField] float playerRespawnDelay = 4f;
        [SerializeField] float botRespawnDelay = 4f;
        [Tooltip("Seconds of invulnerability after spawning.")]
        [SerializeField] float spawnProtection = 1.5f;
        [SerializeField] float resultsDuration = 15f;
        [Tooltip("Longest a dead player may keep the loadout screen open before respawning anyway.")]
        [SerializeField] float maxRespawnHold = 20f;

        [Header("Loadouts")]
        [SerializeField] WeaponDefinition[] primaries = new WeaponDefinition[0];
        [SerializeField] WeaponDefinition[] secondaries = new WeaponDefinition[0];
        [SerializeField] WeaponDefinition[] melees = new WeaponDefinition[0];

        const string LoadoutPrefsKey = "spongykill.loadout.v1";

        readonly NetworkVariable<byte> phase = new NetworkVariable<byte>((byte)MatchPhase.Waiting);
        readonly NetworkVariable<double> phaseEnds = new NetworkVariable<double>();
        readonly NetworkVariable<int> attackerScore = new NetworkVariable<int>();
        readonly NetworkVariable<int> defenderScore = new NetworkVariable<int>();

        readonly int[] loadout = new int[3];
        float deployRequestedAt = float.NegativeInfinity;
        readonly Dictionary<ulong, NetworkPlayer> players = new Dictionary<ulong, NetworkPlayer>();
        readonly Dictionary<ulong, Team> humanTeams = new Dictionary<ulong, Team>();
        readonly Dictionary<NetworkPlayer, double> holdStarted = new Dictionary<NetworkPlayer, double>();

        public static TdmMatch Current { get; private set; }

        public event Action PhaseChanged;
        public event Action ScoreChanged;

        public MatchPhase Phase => (MatchPhase)phase.Value;
        public int ScoreLimit => scoreLimit;
        public float TimeLimit => timeLimit;
        public string MapName => map ? map.DisplayName : "";
        public float TimeRemaining => Phase == MatchPhase.Live ? Remaining : Phase == MatchPhase.Ended ? 0f : timeLimit;
        public float CountdownRemaining => Phase == MatchPhase.Countdown ? Remaining : 0f;
        /// <summary>Seconds until the next match while results are showing.</summary>
        public float NextMatchRemaining => Phase == MatchPhase.Ended ? Remaining : 0f;
        public bool HasLocalPlayer => NetworkPlayer.Local != null;
        /// <summary>Deploy was pressed and the server hasn't spawned the player yet.</summary>
        public bool DeployPending => NetworkPlayer.Local == null && Time.unscaledTime - deployRequestedAt < 5f;
        /// <summary>This machine runs the match and can restart it (the host when playing against bots).</summary>
        public bool CanRestart => IsServer;
        public Team PlayerTeam => TeamMember.LocalPlayer != null ? TeamMember.LocalPlayer.Team : Team.Attackers;
        public bool IsDraw => attackerScore.Value == defenderScore.Value;
        public Team Leader => defenderScore.Value > attackerScore.Value ? Team.Defenders : Team.Attackers;
        public float PlayerRespawnRemaining => NetworkPlayer.Local != null ? NetworkPlayer.Local.RespawnRemaining : -1f;

        /// <summary>While true (the loadout screen is open) a dead local player waits instead of respawning.</summary>
        public bool HoldPlayerRespawn
        {
            get => NetworkPlayer.Local != null && NetworkPlayer.Local.HoldsRespawn;
            set
            {
                if (NetworkPlayer.Local != null) NetworkPlayer.Local.SetHoldRespawn(value);
            }
        }

        public IReadOnlyList<WeaponDefinition> Primaries => primaries;
        public IReadOnlyList<WeaponDefinition> Secondaries => secondaries;
        public IReadOnlyList<WeaponDefinition> Melees => melees;
        public int SelectedPrimary => loadout[0];
        public int SelectedSecondary => loadout[1];
        public int SelectedMelee => loadout[2];

        float Remaining => NetworkManager == null ? 0f : Mathf.Max(0f, (float)(phaseEnds.Value - NetworkManager.ServerTime.Time));
        double Now => NetworkManager.ServerTime.Time;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Current = null;

        public int Score(Team team) => team == Team.Attackers ? attackerScore.Value : defenderScore.Value;

        /// <summary>Everyone on a team with their stats, best first.</summary>
        public List<MemberStats> Standings(Team team)
        {
            var list = new List<MemberStats>();
            foreach (TeamMember member in TeamMember.All)
            {
                if (member.Team != team) continue;
                NetworkCombatant combatant = member.GetComponent<NetworkCombatant>();
                list.Add(combatant ? new MemberStats(member, combatant.Kills, combatant.Deaths, combatant.Headshots) : new MemberStats(member, 0, 0, 0));
            }
            list.Sort((a, b) => b.Score != a.Score ? b.Score.CompareTo(a.Score) : a.Deaths.CompareTo(b.Deaths));
            return list;
        }

        void Awake()
        {
            LoadLoadout();
            if (botSpawner) botSpawner.ManagedByMatch = true;
        }

        public override void OnNetworkSpawn()
        {
            Current = this;
            phase.OnValueChanged += OnPhaseValueChanged;
            attackerScore.OnValueChanged += OnScoreValueChanged;
            defenderScore.OnValueChanged += OnScoreValueChanged;

            if (IsServer)
            {
                if (botSpawner)
                {
                    botSpawner.Objective = BotObjective.Hunt;
                    botSpawner.SetDifficultyLevel(NetSession.BotDifficultyLevel);
                }
                CombatEvents.Eliminated += OnServerElimination;
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            }
            ApplyFreeze();
            PhaseChanged?.Invoke();
            ScoreChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            phase.OnValueChanged -= OnPhaseValueChanged;
            attackerScore.OnValueChanged -= OnScoreValueChanged;
            defenderScore.OnValueChanged -= OnScoreValueChanged;
            if (IsServer)
            {
                CombatEvents.Eliminated -= OnServerElimination;
                if (NetworkManager) NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }
            if (Current == this) Current = null;
            InputGate.MatchFrozen = false;
        }

        void OnPhaseValueChanged(byte previous, byte current)
        {
            ApplyFreeze();
            PhaseChanged?.Invoke();
        }

        void OnScoreValueChanged(int previous, int current) => ScoreChanged?.Invoke();

        void ApplyFreeze() => InputGate.MatchFrozen = Phase != MatchPhase.Live;

        void Update()
        {
            if (spectatorCamera && !Application.isBatchMode)
            {
                bool spectate = NetworkPlayer.Local == null;
                if (spectatorCamera.activeSelf != spectate) spectatorCamera.SetActive(spectate);
            }
            if (!IsSpawned || !IsServer) return;

            switch (Phase)
            {
                case MatchPhase.Countdown:
                    if (Now >= phaseEnds.Value) BeginLive();
                    break;
                case MatchPhase.Live:
                    if (Now >= phaseEnds.Value)
                    {
                        EndMatch();
                        break;
                    }
                    UpdatePlayerRespawns();
                    UpdateBotRespawns();
                    break;
                case MatchPhase.Ended:
                    if (Now >= phaseEnds.Value) RestartMatch();
                    break;
            }
        }

        // ------------------------------------------------------------------ Client requests

        /// <summary>
        /// The local player is ready with the selected loadout: joins the match the first time, or
        /// stops waiting to respawn after choosing new weapons.
        /// </summary>
        public void Deploy()
        {
            if (!IsSpawned) return;
            GameCatalog catalog = GameCatalog.Instance;
            if (NetworkPlayer.Local == null)
            {
                if (catalog == null) return;
                deployRequestedAt = Time.unscaledTime;
                RequestDeployRpc(catalog.WeaponId(Selected(primaries, 0)), catalog.WeaponId(Selected(secondaries, 1)), catalog.WeaponId(Selected(melees, 2)));
                return;
            }
            ApplyLocalLoadout(NetworkPlayer.Local);
            NetworkPlayer.Local.SetHoldRespawn(false);
        }

        /// <summary>Gives the local player the selected weapons (applied on the next spawn).</summary>
        public void ApplyLocalLoadout(NetworkPlayer player)
        {
            player.SetLoadout(Selected(primaries, 0), Selected(secondaries, 1), Selected(melees, 2));
        }

        /// <summary>Host only: start the next match now instead of waiting for the results to finish.</summary>
        public void RestartNow()
        {
            if (IsServer) RestartMatch();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void RequestDeployRpc(byte primary, byte secondary, byte melee, RpcParams rpcParams = default)
        {
            ServerDeploy(rpcParams.Receive.SenderClientId, primary, secondary, melee);
        }

        // ------------------------------------------------------------------ Server: players

        void ServerDeploy(ulong clientId, byte primary, byte secondary, byte melee)
        {
            if (players.ContainsKey(clientId) || playerPrefab == null) return;
            if (!NetworkManager.ConnectedClients.ContainsKey(clientId)) return;

            Team team = PickTeam();
            humanTeams[clientId] = team;
            BalanceBots();

            Transform point = PickSpawn(team);
            Vector3 position = point ? point.position : transform.position;
            Quaternion rotation = point ? point.rotation : Quaternion.identity;
            NetworkObject instance = Instantiate(playerPrefab, position, rotation);
            var player = instance.GetComponent<NetworkPlayer>();
            player.ServerSetLoadout(primary, secondary, melee);
            instance.SpawnAsPlayerObject(clientId, destroyWithScene: true);
            // Network variables can only be written once the object is spawned.
            instance.GetComponent<NetworkCombatant>().ServerConfigure(team, NetSession.NameOf(clientId), UnityEngine.Random.Range(0, 8));
            // The owner moves its own player, so it has to be told where it starts like any respawn.
            player.ServerRespawn(position, rotation, spawnProtection);
            instance.GetComponent<Health>().Died += _ => OnPlayerDied(player);
            players[clientId] = player;

            if (Phase == MatchPhase.Waiting) BeginCountdown(resetScores: true);
        }

        void OnPlayerDied(NetworkPlayer player)
        {
            if (player != null && Phase == MatchPhase.Live) player.ServerSetRespawnTime(Now + playerRespawnDelay);
        }

        void OnClientDisconnected(ulong clientId)
        {
            if (!IsServer) return;
            players.Remove(clientId);
            humanTeams.Remove(clientId);
            if (humanTeams.Count == 0)
            {
                // Nobody left: park the bots until someone deploys again.
                if (botSpawner) botSpawner.SetFrozen(true);
                SetPhase(MatchPhase.Waiting, Now);
                return;
            }
            BalanceBots();
        }

        Team PickTeam()
        {
            int attackers = HumansOn(Team.Attackers);
            int defenders = HumansOn(Team.Defenders);
            if (attackers != defenders) return attackers < defenders ? Team.Attackers : Team.Defenders;
            return attackerScore.Value <= defenderScore.Value ? Team.Attackers : Team.Defenders;
        }

        int HumansOn(Team team)
        {
            int count = 0;
            foreach (Team humanTeam in humanTeams.Values)
                if (humanTeam == team) count++;
            return count;
        }

        void UpdatePlayerRespawns()
        {
            foreach (NetworkPlayer player in players.Values)
            {
                if (player == null || !player.GetComponent<Health>().IsDead) continue;
                float remaining = player.RespawnRemaining;
                if (remaining < 0f)
                {
                    player.ServerSetRespawnTime(Now + playerRespawnDelay);
                    continue;
                }
                if (remaining > 0f) continue;
                // Someone choosing a loadout may wait a little longer, but not forever.
                if (player.HoldsRespawn && WaitedLessThan(player, maxRespawnHold)) continue;
                RespawnPlayer(player);
            }
        }

        bool WaitedLessThan(NetworkPlayer player, float seconds)
        {
            if (!holdStarted.TryGetValue(player, out double started))
            {
                holdStarted[player] = Now;
                return true;
            }
            return Now - started < seconds;
        }

        void RespawnPlayer(NetworkPlayer player)
        {
            holdStarted.Remove(player);
            Transform point = PickSpawn(player.GetComponent<TeamMember>().Team, player.transform);
            if (point == null) return;
            player.ServerRespawn(point.position, point.rotation, spawnProtection);
        }

        // ------------------------------------------------------------------ Server: bots

        /// <summary>Keeps each team at <see cref="teamSize"/> by adding bots or removing them for humans.</summary>
        void BalanceBots()
        {
            if (botSpawner == null) return;
            foreach (Team team in new[] { Team.Attackers, Team.Defenders })
            {
                // Bots only fill teams when playing against bots; online and LAN matches are humans only.
                int wanted = NetSession.Mode == SessionMode.VsBots ? Mathf.Max(0, teamSize - HumansOn(team)) : 0;
                while (botSpawner.CountOn(team) < wanted) SpawnBot(team);
                while (botSpawner.CountOn(team) > wanted) RemoveBot(team);
            }
        }

        void SpawnBot(Team team)
        {
            Transform point = PickSpawn(team);
            if (point == null) return;
            BotController bot = botSpawner.CreateBot(team, point.position, point.rotation, out string botName, out int skin);
            bot.Frozen = Phase != MatchPhase.Live;
            bot.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);
            bot.GetComponent<NetworkCombatant>().ServerConfigure(team, botName, skin);
        }

        void RemoveBot(Team team)
        {
            BotController victim = null;
            foreach (BotController bot in botSpawner.Bots)
            {
                if (bot == null || bot.GetComponent<TeamMember>().Team != team) continue;
                victim = bot;
                if (bot.State == BotState.Dead) break;
            }
            if (victim == null) return;
            botSpawner.Forget(victim);
            NetworkObject networkObject = victim.GetComponent<NetworkObject>();
            if (networkObject.IsSpawned) networkObject.Despawn(true);
            else Destroy(victim.gameObject);
        }

        void UpdateBotRespawns()
        {
            if (botSpawner == null) return;
            foreach (BotController bot in botSpawner.Bots)
                if (bot && bot.State == BotState.Dead && Time.time - bot.DiedAt >= botRespawnDelay) RespawnBot(bot, protect: true);
        }

        void RespawnBot(BotController bot, bool protect)
        {
            Transform point = PickSpawn(bot.GetComponent<TeamMember>().Team, bot.transform);
            if (point == null) return;
            NetworkBot networkBot = bot.GetComponent<NetworkBot>();
            if (networkBot) networkBot.ServerSpawnAt(point.position, point.rotation);
            else bot.Spawn(point.position, point.rotation);
            if (protect) bot.GetComponent<Health>().InvulnerableUntil = Time.time + spawnProtection;
        }

        // ------------------------------------------------------------------ Server: flow and scoring

        void BeginCountdown(bool resetScores)
        {
            if (resetScores)
            {
                attackerScore.Value = 0;
                defenderScore.Value = 0;
                foreach (TeamMember member in TeamMember.All)
                    if (member.TryGetComponent(out NetworkCombatant combatant)) combatant.ServerResetStats();
            }
            BalanceBots();
            if (botSpawner)
            {
                foreach (BotController bot in botSpawner.Bots)
                    if (bot) RespawnBot(bot, protect: false);
                botSpawner.SetFrozen(true);
            }
            foreach (NetworkPlayer player in players.Values)
                if (player) RespawnPlayer(player);
            SetPhase(MatchPhase.Countdown, Now + countdown);
        }

        void BeginLive()
        {
            if (botSpawner) botSpawner.SetFrozen(false);
            SetPhase(MatchPhase.Live, Now + timeLimit);
        }

        void EndMatch()
        {
            if (botSpawner) botSpawner.SetFrozen(true);
            foreach (NetworkPlayer player in players.Values)
                if (player) player.ServerSetRespawnTime(-1d);
            SetPhase(MatchPhase.Ended, Now + resultsDuration);
        }

        void RestartMatch()
        {
            if (humanTeams.Count == 0)
            {
                SetPhase(MatchPhase.Waiting, Now);
                return;
            }
            BeginCountdown(resetScores: true);
        }

        void SetPhase(MatchPhase next, double endsAt)
        {
            phaseEnds.Value = endsAt;
            phase.Value = (byte)next;
            // The server's own listeners run immediately; clients hear through the value change.
            ApplyFreeze();
        }

        void OnServerElimination(EliminationRecord record)
        {
            if (Phase != MatchPhase.Live || record.Victim == null) return;
            if (record.Victim.TryGetComponent(out NetworkCombatant victim)) victim.ServerAddDeath();

            TeamMember killer = record.Killer;
            if (killer == null || killer == record.Victim || killer.Team == record.Victim.Team) return;
            if (killer.TryGetComponent(out NetworkCombatant killerStats)) killerStats.ServerAddKill(record.Headshot);
            if (killer.Team == Team.Attackers) attackerScore.Value++;
            else defenderScore.Value++;
            if (attackerScore.Value >= scoreLimit || defenderScore.Value >= scoreLimit) EndMatch();
        }

        /// <summary>
        /// The team's spawn point farthest from every living enemy, skipping points a teammate stands
        /// on. Near-equal choices are picked at random so spawns don't become predictable.
        /// </summary>
        Transform PickSpawn(Team team, Transform ignore = null)
        {
            if (map == null) return null;
            IReadOnlyList<Transform> points = map.SpawnFor(team).Points;
            if (points.Count == 0) return null;

            var scored = new List<(Transform point, float safety)>();
            foreach (Transform point in points)
            {
                bool occupied = false;
                float nearestEnemy = 1000f;
                foreach (TeamMember member in TeamMember.All)
                {
                    if (!member.IsAlive || member.transform == ignore) continue;
                    float distance = Vector3.Distance(member.transform.position, point.position);
                    if (member.Team == team) occupied |= distance < 1.2f;
                    else nearestEnemy = Mathf.Min(nearestEnemy, distance);
                }
                if (!occupied) scored.Add((point, nearestEnemy));
            }
            if (scored.Count == 0) return points[UnityEngine.Random.Range(0, points.Count)];

            float best = 0f;
            foreach ((Transform _, float safety) in scored) best = Mathf.Max(best, safety);
            List<(Transform point, float safety)> good = scored.FindAll(entry => entry.safety >= best - 5f);
            return good[UnityEngine.Random.Range(0, good.Count)].point;
        }

        // ------------------------------------------------------------------ Loadout (local)

        public void SelectLoadout(int primary, int secondary, int melee)
        {
            loadout[0] = Mathf.Clamp(primary, 0, Mathf.Max(0, primaries.Length - 1));
            loadout[1] = Mathf.Clamp(secondary, 0, Mathf.Max(0, secondaries.Length - 1));
            loadout[2] = Mathf.Clamp(melee, 0, Mathf.Max(0, melees.Length - 1));
            PlayerPrefs.SetString(LoadoutPrefsKey, $"{Name(primaries, loadout[0])}|{Name(secondaries, loadout[1])}|{Name(melees, loadout[2])}");
            PlayerPrefs.Save();
        }

        WeaponDefinition Selected(WeaponDefinition[] list, int slot) => Pick(list, loadout[slot]);

        /// <summary>Saved by weapon name so reordering the lists doesn't swap someone's choice.</summary>
        void LoadLoadout()
        {
            string[] names = PlayerPrefs.GetString(LoadoutPrefsKey, "").Split('|');
            loadout[0] = IndexOf(primaries, names.Length > 0 ? names[0] : null);
            loadout[1] = IndexOf(secondaries, names.Length > 1 ? names[1] : null);
            loadout[2] = IndexOf(melees, names.Length > 2 ? names[2] : null);
        }

        static WeaponDefinition Pick(WeaponDefinition[] list, int index) => list.Length == 0 ? null : list[Mathf.Clamp(index, 0, list.Length - 1)];

        static string Name(WeaponDefinition[] list, int index) => Pick(list, index) ? Pick(list, index).name : "";

        static int IndexOf(WeaponDefinition[] list, string assetName)
        {
            for (int i = 0; i < list.Length; i++)
                if (list[i] && list[i].name == assetName) return i;
            return 0;
        }
    }
}
