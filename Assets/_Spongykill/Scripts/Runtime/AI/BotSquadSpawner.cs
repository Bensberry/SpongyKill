using System.Collections.Generic;
using Spongykill.Characters;
using Spongykill.Combat;
using Spongykill.Core;
using Spongykill.Maps;
using Spongykill.Weapons;
using UnityEngine;

namespace Spongykill.AI
{
    /// <summary>
    /// Makes and keeps track of bots: picks a free name, a skin and a weighted loadout. On its own it
    /// fills both teams at start and respawns them; a match that sets <see cref="ManagedByMatch"/>
    /// instead creates and removes bots one at a time (to make room for human players).
    /// </summary>
    public class BotSquadSpawner : MonoBehaviour
    {
        [SerializeField] MapDefinition map;
        [SerializeField] BotController botPrefab;
        [SerializeField] BotDifficulty difficulty;
        [Tooltip("Easy, Normal and Hard, selectable from the main menu for games against bots.")]
        [SerializeField] BotDifficulty[] difficultyLevels = new BotDifficulty[0];
        [Tooltip("Bots on the player's team when running without a match (the player is an attacker).")]
        [SerializeField, Range(0, 9)] int attackerBots = 4;
        [SerializeField, Range(0, 10)] int defenderBots = 5;
        [SerializeField] bool respawnBots = true;
        [SerializeField] float respawnDelay = 5f;

        [Header("Looks")]
        [SerializeField] CharacterSkin[] attackerSkins = new CharacterSkin[0];
        [SerializeField] CharacterSkin[] defenderSkins = new CharacterSkin[0];

        [Header("Loadouts")]
        [SerializeField] WeaponDefinition[] primaryWeapons = new WeaponDefinition[0];
        [Tooltip("Relative chance of each primary weapon, same order as the list above.")]
        [SerializeField] float[] primaryWeights = new float[0];
        [SerializeField] WeaponDefinition secondaryWeapon;

        static readonly string[] AttackerNames = { "Barnacle", "Gill", "Riptide", "Squall", "Wade", "Dredge", "Brackish", "Undertow", "Bilge" };
        static readonly string[] DefenderNames = { "Bubbles", "Coralie", "Finn", "Marina", "Kelpie", "Reefy", "Nautilus", "Sandbar", "Tidepool", "Lagoon" };

        readonly List<BotController> bots = new List<BotController>();
        BotObjective objective = BotObjective.Sites;

        public IReadOnlyList<BotController> Bots => bots;
        public MapDefinition Map => map;

        /// <summary>Set before Start by a match to take over spawning and respawning.</summary>
        public bool ManagedByMatch { get; set; }

        public BotObjective Objective
        {
            get => objective;
            set
            {
                objective = value;
                foreach (BotController bot in bots) bot.Objective = value;
            }
        }

        public BotDifficulty Difficulty
        {
            get => difficulty;
            set
            {
                difficulty = value;
                foreach (BotController bot in bots) bot.Difficulty = value;
            }
        }

        /// <summary>0 = Easy, 1 = Normal, 2 = Hard (when those assets are assigned).</summary>
        public void SetDifficultyLevel(int level)
        {
            if (difficultyLevels.Length == 0) return;
            Difficulty = difficultyLevels[Mathf.Clamp(level, 0, difficultyLevels.Length - 1)];
        }

        void Start()
        {
            if (!ManagedByMatch) SpawnAll();
        }

        /// <summary>Without a match: creates both teams once.</summary>
        public void SpawnAll()
        {
            if (map == null || botPrefab == null || bots.Count > 0) return;
            for (int i = 0; i < attackerBots; i++) SpawnAtPoint(Team.Attackers, i + 1);
            for (int i = 0; i < defenderBots; i++) SpawnAtPoint(Team.Defenders, i);
        }

        void SpawnAtPoint(Team team, int pointIndex)
        {
            IReadOnlyList<Transform> points = map.SpawnFor(team).Points;
            Transform point = points[pointIndex % points.Count];
            BotController bot = CreateBot(team, point.position, point.rotation, out _, out int skin);
            CharacterSkin[] skins = SkinsFor(team);
            if (skins.Length > 0 && bot.TryGetComponent(out CharacterAppearance appearance)) appearance.Apply(skins[skin % skins.Length]);
        }

        /// <summary>
        /// A new bot alive at the given point with a free name, a skin index and a loadout. Online, the
        /// caller still has to spawn its NetworkObject.
        /// </summary>
        public BotController CreateBot(Team team, Vector3 position, Quaternion rotation, out string botName, out int skinIndex)
        {
            botName = FreeName(team);
            skinIndex = Random.Range(0, Mathf.Max(1, SkinsFor(team).Length));
            BotController bot = Instantiate(botPrefab, position, rotation, transform);
            bot.name = $"Bot_{team}_{botName}";
            bot.GetComponent<TeamMember>().Configure(team, botName);
            bot.GetComponent<BotWeapons>().SetLoadout(PickPrimary(), secondaryWeapon);
            bot.Difficulty = difficulty;
            bot.Objective = objective;
            bot.Spawn(position, rotation);
            bots.Add(bot);
            return bot;
        }

        /// <summary>Forgets a bot; the caller destroys or despawns it.</summary>
        public void Forget(BotController bot) => bots.Remove(bot);

        public int CountOn(Team team)
        {
            int count = 0;
            foreach (BotController bot in bots)
                if (bot && bot.GetComponent<TeamMember>().Team == team) count++;
            return count;
        }

        public void SetFrozen(bool frozen)
        {
            foreach (BotController bot in bots)
                if (bot) bot.Frozen = frozen;
        }

        void Update()
        {
            if (!respawnBots || ManagedByMatch) return;
            foreach (BotController bot in bots)
            {
                if (bot.State != BotState.Dead || Time.time - bot.DiedAt < respawnDelay) continue;
                IReadOnlyList<Transform> points = map.SpawnFor(bot.GetComponent<TeamMember>().Team).Points;
                Transform point = points[Random.Range(0, points.Count)];
                bot.Spawn(point.position, point.rotation);
            }
        }

        CharacterSkin[] SkinsFor(Team team) => team == Team.Attackers ? attackerSkins : defenderSkins;

        string FreeName(Team team)
        {
            string[] names = team == Team.Attackers ? AttackerNames : DefenderNames;
            foreach (string candidate in names)
            {
                bool taken = false;
                foreach (BotController bot in bots)
                    if (bot && bot.GetComponent<TeamMember>().DisplayName == candidate) taken = true;
                if (!taken) return candidate;
            }
            return names[Random.Range(0, names.Length)] + Random.Range(2, 99);
        }

        WeaponDefinition PickPrimary()
        {
            if (primaryWeapons.Length == 0) return null;
            float total = 0f;
            for (int i = 0; i < primaryWeapons.Length; i++) total += Weight(i);
            float roll = Random.value * total;
            for (int i = 0; i < primaryWeapons.Length; i++)
            {
                roll -= Weight(i);
                if (roll <= 0f) return primaryWeapons[i];
            }
            return primaryWeapons[primaryWeapons.Length - 1];
        }

        float Weight(int index) => index < primaryWeights.Length ? Mathf.Max(0f, primaryWeights[index]) : 1f;
    }
}
