using System.Collections.Generic;
using Spongykill.AI;
using Spongykill.Weapons;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.AI;

namespace Spongykill.Net
{
    /// <summary>
    /// Bots think only on the server. Clients get a puppet: its transform, pose and weapon come from
    /// the server, and its shots arrive as effects to draw.
    /// </summary>
    [RequireComponent(typeof(BotController), typeof(NetworkAvatar))]
    public class NetworkBot : NetworkBehaviour
    {
        [SerializeField] NetworkTransform networkTransform;

        readonly List<PelletResult> pellets = new List<PelletResult>();
        BotController brain;
        BotWeapons weapons;
        NavMeshAgent agent;
        NetworkAvatar avatar;
        Vector3 shotMuzzle;

        public BotController Brain => brain;

        void Awake()
        {
            brain = GetComponent<BotController>();
            weapons = GetComponent<BotWeapons>();
            agent = GetComponent<NavMeshAgent>();
            avatar = GetComponent<NetworkAvatar>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
            {
                brain.enabled = false;
                if (weapons) weapons.enabled = false;
                if (agent) agent.enabled = false;
                return;
            }
            if (weapons == null) return;
            weapons.Equipped += OnEquipped;
            weapons.PelletFired += OnPelletFired;
            weapons.Fired += OnFired;
            OnEquipped(weapons.Current?.Definition);
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer || weapons == null) return;
            weapons.Equipped -= OnEquipped;
            weapons.PelletFired -= OnPelletFired;
            weapons.Fired -= OnFired;
        }

        void Update()
        {
            if (!IsServer || !IsSpawned) return;
            avatar.Publish(weapons ? weapons.Current?.Definition : null, brain.AimPitch, brain.IsCrouching);
        }

        /// <summary>Server: places the bot alive at a spawn point without clients sliding it there.</summary>
        public void ServerSpawnAt(Vector3 position, Quaternion rotation)
        {
            brain.Spawn(position, rotation);
            if (networkTransform && IsSpawned) networkTransform.Teleport(transform.position, transform.rotation, transform.localScale);
        }

        void OnEquipped(WeaponDefinition weapon) => avatar.Publish(weapon, brain.AimPitch, brain.IsCrouching);

        void OnPelletFired(Vector3 muzzle, PelletResult pellet)
        {
            shotMuzzle = muzzle;
            pellets.Add(pellet);
        }

        void OnFired(WeaponDefinition weapon)
        {
            GameCatalog catalog = GameCatalog.Instance;
            if (catalog != null) ShotEffectsRpc(NetworkAvatar.Pack(catalog.WeaponId(weapon), shotMuzzle, pellets));
            pellets.Clear();
        }

        [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
        void ShotEffectsRpc(ShotEffects shot) => avatar.PlayShot(shot);
    }
}
