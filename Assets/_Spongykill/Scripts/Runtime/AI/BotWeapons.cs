using System;
using Spongykill.Audio;
using Spongykill.Effects;
using Spongykill.Weapons;
using UnityEngine;

namespace Spongykill.AI
{
    /// <summary>
    /// A bot's guns: primary and secondary, ammo and reloads, firing through the same
    /// <see cref="WeaponFiring"/> as the player, and the third-person weapon model with its muzzle flash.
    /// </summary>
    public class BotWeapons : MonoBehaviour
    {
        [SerializeField] Transform weaponMount;
        [SerializeField] AudioSource audioSource;
        [Tooltip("What shots can hit. Exclude movement capsules (Player, Bot) and viewmodels.")]
        [SerializeField] LayerMask hitMask = Physics.DefaultRaycastLayers;

        WeaponRuntime primary;
        WeaponRuntime secondary;
        GameObject model;
        WeaponViewmodel modelViewmodel;
        float reloadDoneTime = -1f;
        Action<PelletResult> pelletHandler;
        Vector3 shotMuzzle;

        /// <summary>The weapon in hand changed (null when unarmed).</summary>
        public event Action<WeaponDefinition> Equipped;
        /// <summary>One traced pellet of a shot, with the muzzle it left from (for network effects).</summary>
        public event Action<Vector3, PelletResult> PelletFired;
        /// <summary>A shot was fired with this weapon.</summary>
        public event Action<WeaponDefinition> Fired;

        public WeaponRuntime Current { get; private set; }
        public bool IsReloading => reloadDoneTime >= 0f;
        public bool CanFire => Current != null && !IsReloading && Current.HasAmmo && Time.time >= Current.NextFireTime;

        void Awake() => pelletHandler = OnPellet;

        public void SetLoadout(WeaponDefinition primaryWeapon, WeaponDefinition secondaryWeapon)
        {
            primary = primaryWeapon ? new WeaponRuntime(primaryWeapon) : null;
            secondary = secondaryWeapon ? new WeaponRuntime(secondaryWeapon) : null;
            Equip(primary ?? secondary);
        }

        public void RefillAll()
        {
            primary?.Refill();
            secondary?.Refill();
            reloadDoneTime = -1f;
            Equip(primary ?? secondary);
        }

        /// <summary>Switch to the pistol when the primary is dry (faster than reloading mid-fight).</summary>
        public bool SwitchToSecondaryIfDry()
        {
            if (Current != primary || secondary == null || primary.HasAmmo || !secondary.HasAmmo) return false;
            Equip(secondary);
            return true;
        }

        public void SwitchToPrimaryIfLoaded()
        {
            if (Current == secondary && primary != null && (primary.HasAmmo || primary.CanReload)) Equip(primary);
        }

        public void Tick(float deltaTime)
        {
            if (Current == null) return;
            Current.RecoverBloom(Current.Definition.bloomRecovery * deltaTime);
            if (IsReloading && Time.time >= reloadDoneTime)
            {
                reloadDoneTime = -1f;
                Current.FinishReload();
            }
        }

        public void StartReload()
        {
            if (Current == null || IsReloading || !Current.CanReload) return;
            reloadDoneTime = Time.time + Current.Definition.reloadTime;
            Play(ProceduralSfx.ReloadStart, 0.6f);
        }

        /// <summary>Fires one trigger pull from the bot's eyes toward <paramref name="direction"/>.</summary>
        public ShotReport Fire(Vector3 origin, Vector3 direction, float spread)
        {
            WeaponRuntime weapon = Current;
            weapon.TryConsumeRound();
            float interval = weapon.Definition.FireInterval;
            weapon.NextFireTime = Time.time - weapon.NextFireTime < interval ? weapon.NextFireTime + interval : Time.time + interval;

            shotMuzzle = modelViewmodel ? modelViewmodel.Muzzle.position : origin;
            ShotReport report = WeaponFiring.Fire(weapon.Definition, origin, direction, spread, gameObject, hitMask, pelletHandler);
            weapon.AddBloom(weapon.Definition.spreadPerShot);
            if (modelViewmodel) modelViewmodel.Flash();
            WeaponDefinition definition = weapon.Definition;
            Play(ProceduralSfx.Shot(definition.shotSoundPitch, definition.shotSoundLength, definition.shotSoundNoise), 1f);
            Fired?.Invoke(definition);
            return report;
        }

        void Equip(WeaponRuntime weapon)
        {
            Current = weapon;
            reloadDoneTime = -1f;
            modelViewmodel = ThirdPersonWeapon.Attach(weaponMount, weapon?.Definition, ref model);
            Equipped?.Invoke(weapon?.Definition);
        }

        void OnPellet(PelletResult pellet)
        {
            PelletFired?.Invoke(shotMuzzle, pellet);
            CombatEffects effects = CombatEffects.Instance;
            if (effects == null || Current == null) return;
            if (!Current.IsMelee) effects.Tracer(shotMuzzle, pellet.EndPoint, Current.Definition.tracerColor);
            if (pellet.HitSomething) effects.Impact(pellet.EndPoint, pellet.Normal, pellet.HitCharacter);
        }

        void Play(AudioClip clip, float volume)
        {
            if (audioSource && clip) audioSource.PlayOneShot(clip, volume);
        }
    }
}
