using System.Collections.Generic;
using Spongykill.Characters;
using Spongykill.Combat;
using Spongykill.Core;
using Spongykill.Maps;
using Spongykill.Weapons;
using UnityEngine;
using UnityEngine.AI;

namespace Spongykill.AI
{
    /// <summary>What a bot does when no enemy is around.</summary>
    public enum BotObjective
    {
        /// <summary>Defenders hold angles on the sites; attackers push a lane and hold a site.</summary>
        Sites,
        /// <summary>Team deathmatch: keep moving toward where the enemy team is.</summary>
        Hunt,
    }

    public enum BotState
    {
        Roam,
        Hold,
        Engage,
        Chase,
        Investigate,
        TakeCover,
        Dead,
    }

    /// <summary>
    /// A bot's brain. Perceives enemies by distance, view cone and line of sight (plus hearing and
    /// being shot), aims with a limited turn speed and an error that settles over time, fires in
    /// bursts through the shared weapon code, strafes, chases the last known position, ducks into cover
    /// to reload or when hurt, and otherwise works through tactical points: defenders hold angles,
    /// attackers push a lane and hold a site, or in team deathmatch everyone hunts the other team.
    /// All sharpness comes from a <see cref="BotDifficulty"/>.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent), typeof(Health), typeof(TeamMember))]
    public class BotController : MonoBehaviour
    {
        [SerializeField] BotDifficulty difficulty;
        [SerializeField] BotWeapons weapons;
        [SerializeField] CharacterAnimator characterAnimator;
        [SerializeField] HitboxRig hitboxRig;
        [Tooltip("Movement blocker; switched off while dead.")]
        [SerializeField] Collider bodyBlocker;
        [Tooltip("What blocks sight: world geometry and hitboxes (not movement capsules).")]
        [SerializeField] LayerMask sightMask = Physics.DefaultRaycastLayers;
        [Tooltip("What counts as cover: solid world geometry.")]
        [SerializeField] LayerMask coverMask = 1;
        [SerializeField] float walkSpeed = 4.8f;
        [SerializeField] BotObjective objective = BotObjective.Sites;

        const float StandingEyeHeight = 1.6f;
        const float CrouchingEyeHeight = 1.05f;
        const float PerceptionInterval = 0.15f;
        const float ChaseMemory = 5f;
        const float AimTolerance = 2.5f;

        NavMeshAgent agent;
        Health health;
        TeamMember self;

        TeamMember target;
        bool targetVisible;
        Vector3 lastKnownPosition;
        float lastSeenTime = float.NegativeInfinity;
        float targetAcquiredTime;
        Vector2 aimErrorAngles;
        bool aimForHead;
        float wobbleSeed;

        float aimYaw;
        float aimPitch;

        int burstShotsLeft;
        float burstResumeTime;
        float nextSemiShotTime;

        TacticalPoint goal;
        string attackSite;
        bool onApproach;
        float holdUntil;
        float nextGoalTime;

        Vector3 investigatePosition;
        float investigateUntil;
        float nextPerceptionTime;

        bool strafing;
        bool strafeMoving;
        float strafePhaseEnd;
        Vector3 coverPosition;
        float coverUntil;
        float lastCoverTime = float.NegativeInfinity;
        bool crouching;

        public BotState State { get; private set; }
        /// <summary>Degrees the bot is aiming up or down (positive looks down), for remote animation.</summary>
        public float AimPitch => aimPitch;
        public bool IsCrouching => crouching;
        public TeamMember Target => target;
        public float DiedAt { get; private set; } = float.NegativeInfinity;

        public BotDifficulty Difficulty
        {
            get => difficulty;
            set => difficulty = value;
        }

        public BotObjective Objective
        {
            get => objective;
            set
            {
                if (objective == value) return;
                objective = value;
                ReleaseGoal();
                attackSite = null;
            }
        }

        /// <summary>Stands still and does nothing (match countdown and results).</summary>
        public bool Frozen { get; set; }

        Vector3 Eye => transform.position + Vector3.up * (crouching ? CrouchingEyeHeight : StandingEyeHeight);
        Vector3 AimForward => Quaternion.Euler(aimPitch, aimYaw, 0f) * Vector3.forward;
        bool Arrived => !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.25f;

        float MoveSpeed
        {
            get
            {
                float weaponFactor = weapons.Current != null ? weapons.Current.Definition.moveSpeedFactor : 1f;
                return walkSpeed * weaponFactor * (crouching ? 0.5f : 1f);
            }
        }

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<Health>();
            self = GetComponent<TeamMember>();
            agent.updateRotation = false;
            wobbleSeed = Random.Range(0f, 100f);
        }

        void OnEnable()
        {
            health.Damaged += OnDamaged;
            health.Died += OnDied;
            CombatEvents.Noise += OnNoise;
        }

        void OnDisable()
        {
            health.Damaged -= OnDamaged;
            health.Died -= OnDied;
            CombatEvents.Noise -= OnNoise;
            ReleaseGoal();
        }

        /// <summary>Places the bot alive and fully stocked at a spawn point.</summary>
        public void Spawn(Vector3 position, Quaternion rotation)
        {
            if (!agent.enabled) agent.enabled = true;
            agent.Warp(position);
            aimYaw = rotation.eulerAngles.y;
            aimPitch = 0f;
            transform.rotation = Quaternion.Euler(0f, aimYaw, 0f);

            health.ResetHealth();
            weapons.RefillAll();
            characterAnimator.ResetPose();
            if (bodyBlocker) bodyBlocker.enabled = true;
            SetCrouch(false);

            target = null;
            targetVisible = false;
            lastSeenTime = float.NegativeInfinity;
            investigateUntil = 0f;
            ReleaseGoal();
            attackSite = null;
            onApproach = false;
            nextGoalTime = Time.time + Random.Range(0f, 1.5f);
            State = BotState.Roam;
        }

        void Update()
        {
            if (State == BotState.Dead || !agent.enabled || difficulty == null) return;
            float deltaTime = Time.deltaTime;
            if (Frozen)
            {
                Stop();
                characterAnimator.Tick(Vector3.zero, aimPitch, crouching, deltaTime);
                return;
            }

            weapons.Tick(deltaTime);
            if (Time.time >= nextPerceptionTime)
            {
                nextPerceptionTime = Time.time + PerceptionInterval * Random.Range(0.8f, 1.2f);
                Perceive();
            }
            Decide();
            Act();
            UpdateAim(deltaTime);
            agent.speed = MoveSpeed;
            characterAnimator.Tick(agent.velocity, aimPitch, crouching, deltaTime);
        }

        // ------------------------------------------------------------------ Perception

        void Perceive()
        {
            Vector3 eye = Eye;
            Vector3 forward = AimForward;
            TeamMember best = null;
            float bestDistance = float.MaxValue;

            foreach (TeamMember other in TeamMember.All)
            {
                // Spawn-protected enemies are left alone so nobody is killed the moment they appear.
                if (other == self || other.Team == self.Team || !other.IsAlive || other.Health.IsInvulnerable) continue;
                Vector3 toTarget = other.ChestPosition - eye;
                float distance = toTarget.magnitude;
                if (distance > difficulty.viewDistance) continue;

                // Someone just seen (or who just shot us) stays tracked even outside the cone.
                bool tracked = other == target && Time.time - lastSeenTime < 1f;
                if (!tracked && distance > 4f && Vector3.Angle(forward, toTarget) > difficulty.viewAngle * 0.5f) continue;
                if (!CanSee(other, eye)) continue;
                if (distance < bestDistance)
                {
                    best = other;
                    bestDistance = distance;
                }
            }

            targetVisible = best != null;
            if (best == null) return;
            if (best != target || Time.time - lastSeenTime > 1.5f) AcquireTarget(best);
            lastSeenTime = Time.time;
            lastKnownPosition = best.transform.position;
        }

        bool CanSee(TeamMember other, Vector3 eye) => RayReaches(other, eye, other.HeadPosition) || RayReaches(other, eye, other.ChestPosition);

        bool RayReaches(TeamMember other, Vector3 eye, Vector3 point)
        {
            Vector3 direction = point - eye;
            float distance = direction.magnitude;
            if (distance < 0.01f) return true;
            if (!Hitscan.Trace(eye, direction / distance, distance + 0.5f, 0f, sightMask, transform, out RaycastHit hit)) return false;
            return hit.collider.TryGetComponent(out Hitbox hitbox) && hitbox.Owner == other.Health;
        }

        void AcquireTarget(TeamMember newTarget)
        {
            target = newTarget;
            targetAcquiredTime = Time.time;
            aimForHead = Random.value < difficulty.headshotChance;
            aimErrorAngles = Random.insideUnitCircle.normalized * difficulty.aimError * Random.Range(0.6f, 1f);
            burstShotsLeft = difficulty.burstShots;
            strafing = Random.value < difficulty.strafeChance;
        }

        // ------------------------------------------------------------------ Decisions

        void Decide()
        {
            if (target != null && !target.IsAlive)
            {
                target = null;
                targetVisible = false;
            }

            if (State == BotState.TakeCover)
            {
                bool stillNeeded = Time.time < coverUntil || weapons.IsReloading;
                if (stillNeeded) return;
                SetCrouch(false);
                State = target != null ? BotState.Chase : BotState.Roam;
            }

            if (target != null && targetVisible)
            {
                if (ShouldTakeCover() && TryStartCover()) return;
                if (State == BotState.Hold || State == BotState.Roam) ReleaseGoal();
                State = BotState.Engage;
                return;
            }
            if (target != null && Time.time - lastSeenTime < ChaseMemory)
            {
                State = BotState.Chase;
                return;
            }
            target = null;
            if (Time.time < investigateUntil)
            {
                State = BotState.Investigate;
                return;
            }
            if (State != BotState.Hold) State = BotState.Roam;
        }

        bool ShouldTakeCover()
        {
            if (Time.time - lastCoverTime < 5f) return false;
            float distance = Vector3.Distance(transform.position, target.transform.position);
            bool dry = weapons.IsReloading || (weapons.Current != null && !weapons.Current.HasAmmo);
            bool hurt = health.Current < difficulty.coverHealthThreshold * health.Max;
            return (dry && distance < 30f) || hurt;
        }

        bool TryStartCover()
        {
            if (!FindCover(target.HeadPosition, out Vector3 cover)) return false;
            coverPosition = cover;
            coverUntil = Time.time + Random.Range(2f, 3.5f);
            lastCoverTime = Time.time;
            ReleaseGoal();
            State = BotState.TakeCover;
            if (weapons.Current != null && weapons.Current.Magazine < weapons.Current.Definition.magazineSize * 0.6f) weapons.StartReload();
            return true;
        }

        /// <summary>Nearest NavMesh spot, in growing rings, where solid geometry hides a crouching body from the threat.</summary>
        bool FindCover(Vector3 threatEye, out Vector3 cover)
        {
            cover = default;
            Vector3 origin = transform.position;
            for (int ring = 1; ring <= 3; ring++)
            {
                float radius = ring * 2.5f;
                float bestScore = float.MaxValue;
                for (int i = 0; i < 10; i++)
                {
                    Vector3 candidate = origin + Quaternion.Euler(0f, i * 36f + ring * 12f, 0f) * Vector3.forward * radius;
                    if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1.2f, NavMesh.AllAreas)) continue;
                    Vector3 crouchedChest = hit.position + Vector3.up * 0.95f;
                    if (!Physics.Linecast(threatEye, crouchedChest, coverMask, QueryTriggerInteraction.Ignore)) continue;
                    float score = Vector3.Distance(origin, hit.position) + (Vector3.Distance(hit.position, threatEye) < 6f ? 10f : 0f);
                    if (score >= bestScore) continue;
                    bestScore = score;
                    cover = hit.position;
                }
                if (bestScore < float.MaxValue) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ Actions

        void Act()
        {
            switch (State)
            {
                case BotState.Engage:
                    ActEngage();
                    break;
                case BotState.Chase:
                    SetCrouch(false);
                    MoveTo(lastKnownPosition);
                    if (Arrived) lastSeenTime = float.NegativeInfinity;
                    break;
                case BotState.Investigate:
                    SetCrouch(false);
                    MoveTo(investigatePosition);
                    if (Arrived) investigateUntil = 0f;
                    break;
                case BotState.TakeCover:
                    MoveTo(coverPosition);
                    if (Arrived)
                    {
                        SetCrouch(true);
                        if (weapons.Current != null && !weapons.Current.HasAmmo) weapons.StartReload();
                    }
                    break;
                default:
                    ActOnGoal();
                    break;
            }
        }

        void ActEngage()
        {
            SetCrouch(false);
            WeaponRuntime weapon = weapons.Current;
            if (weapon == null) return;
            if (!weapon.HasAmmo && !weapons.SwitchToSecondaryIfDry()) weapons.StartReload();
            weapon = weapons.Current;

            WeaponDefinition definition = weapon.Definition;
            float distance = Vector3.Distance(transform.position, target.transform.position);
            float preferredRange = definition.category == WeaponCategory.Shotgun ? 9f
                : definition.category == WeaponCategory.SMG ? 25f
                : definition.IsMelee ? 1.5f
                : 60f;

            if (distance > preferredRange) MoveTo(target.transform.position);
            else if (strafing && definition.category != WeaponCategory.Sniper) Strafe();
            else Stop();

            TryShoot(distance);
        }

        /// <summary>Short side-steps with pauses in between, shooting best while paused.</summary>
        void Strafe()
        {
            if (Time.time < strafePhaseEnd) return;
            strafeMoving = !strafeMoving;
            if (!strafeMoving)
            {
                strafePhaseEnd = Time.time + Random.Range(0.4f, 0.9f);
                Stop();
                return;
            }
            strafePhaseEnd = Time.time + Random.Range(0.35f, 0.6f);
            Vector3 toTarget = target.transform.position - transform.position;
            toTarget.y = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, toTarget.normalized) * ((Random.value < 0.5f ? -1f : 1f) * Random.Range(1.5f, 3f));
            if (NavMesh.SamplePosition(transform.position + side, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) MoveTo(hit.position);
        }

        void TryShoot(float distance)
        {
            if (!weapons.CanFire) return;
            if (Time.time - targetAcquiredTime < difficulty.reactionTime) return;
            if (Vector3.Angle(AimForward, DesiredAimDirection()) > AimTolerance) return;

            WeaponDefinition definition = weapons.Current.Definition;
            if (definition.IsMelee && distance > definition.range + 0.3f) return;

            if (definition.fireMode == FireMode.Automatic)
            {
                if (Time.time < burstResumeTime) return;
                if (burstShotsLeft <= 0)
                {
                    burstShotsLeft = difficulty.burstShots;
                    burstResumeTime = Time.time + difficulty.burstPause * Random.Range(0.7f, 1.3f);
                    return;
                }
                burstShotsLeft--;
            }
            else
            {
                if (Time.time < nextSemiShotTime) return;
                nextSemiShotTime = Time.time + definition.FireInterval + difficulty.semiAutoDelay * Random.Range(0.6f, 1.4f);
            }

            float speed = new Vector2(agent.velocity.x, agent.velocity.z).magnitude;
            float aimBlend = definition.aimUsesScope && speed < 0.5f ? 1f : definition.canAim ? 0.4f : 0f;
            float spread = WeaponSpread.Compute(definition, aimBlend, speed, walkSpeed, true, crouching, weapons.Current.Bloom) * difficulty.spreadMultiplier;
            weapons.Fire(Eye, AimForward, spread);
        }

        void ActOnGoal()
        {
            SetCrouch(false);
            if (goal == null)
            {
                if (Time.time >= nextGoalTime) PickGoal();
                if (goal == null)
                {
                    Stop();
                    return;
                }
            }

            Vector3 toGoal = goal.transform.position - transform.position;
            toGoal.y = 0f;
            if (toGoal.magnitude > 0.8f && State != BotState.Hold)
            {
                State = BotState.Roam;
                MoveTo(goal.transform.position);
                return;
            }
            if (State != BotState.Hold)
            {
                State = BotState.Hold;
                Stop();
                holdUntil = Time.time + HoldDuration();
            }
            if (Time.time >= holdUntil) AdvanceGoal();
        }

        float HoldDuration()
        {
            if (objective == BotObjective.Hunt) return Random.Range(0.8f, 4f);
            if (self.Team == Team.Defenders) return Random.Range(35f, 70f);
            return onApproach ? Random.Range(0.5f, 2.5f) : Random.Range(18f, 30f);
        }

        void PickGoal()
        {
            if (objective == BotObjective.Hunt)
            {
                goal = PickHuntPoint();
                onApproach = false;
                return;
            }

            if (self.Team == Team.Defenders)
            {
                goal = PickPoint(TacticalRole.DefenderHold, null, reserve: true);
                onApproach = false;
                return;
            }

            // Attackers: choose a site, pass through one of its approaches, then hold inside it.
            if (attackSite == null)
            {
                attackSite = Random.value < 0.5f ? "A" : "B";
                goal = PickPoint(TacticalRole.AttackerApproach, attackSite, reserve: false);
                onApproach = goal != null;
                if (goal != null) return;
            }
            goal = PickPoint(TacticalRole.AttackerSiteHold, attackSite, reserve: true);
            onApproach = false;
        }

        /// <summary>
        /// Usually the tactical point closest to a living enemy (bots in deathmatch roam toward the
        /// fight), sometimes a random one so routes vary. Points right next to the bot are skipped.
        /// </summary>
        TacticalPoint PickHuntPoint()
        {
            var candidates = new List<TacticalPoint>();
            foreach (TacticalPoint point in TacticalPoint.All)
                if ((point.transform.position - transform.position).sqrMagnitude > 36f) candidates.Add(point);
            if (candidates.Count == 0) return null;

            TeamMember prey = null;
            if (Random.value < 0.65f)
            {
                var enemies = new List<TeamMember>();
                foreach (TeamMember member in TeamMember.All)
                    if (member.Team != self.Team && member.IsAlive && !NearOwnSpawn(member)) enemies.Add(member);
                if (enemies.Count > 0) prey = enemies[Random.Range(0, enemies.Count)];
            }
            if (prey == null) return candidates[Random.Range(0, candidates.Count)];

            TacticalPoint nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (TacticalPoint point in candidates)
            {
                float distance = (point.transform.position - prey.transform.position).sqrMagnitude;
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearest = point;
            }
            return nearest;
        }

        /// <summary>Enemies still in their spawn aren't hunted there (no spawn camping).</summary>
        static bool NearOwnSpawn(TeamMember member)
        {
            foreach (SpawnArea area in SpawnArea.All)
            {
                if (area.Team != member.Team) continue;
                foreach (Transform point in area.Points)
                    if ((point.position - member.transform.position).sqrMagnitude < 144f) return true;
            }
            return false;
        }

        void AdvanceGoal()
        {
            bool finishedSiteHold = self.Team == Team.Attackers && !onApproach;
            ReleaseGoal();
            State = BotState.Roam;
            if (finishedSiteHold) attackSite = null;
            nextGoalTime = Time.time + (onApproach ? 0f : Random.Range(0.5f, 2f));
            onApproach = false;
        }

        TacticalPoint PickPoint(TacticalRole role, string site, bool reserve)
        {
            var candidates = new List<TacticalPoint>();
            foreach (TacticalPoint point in TacticalPoint.All)
            {
                if (point.Role != role) continue;
                if (site != null && point.Site != site) continue;
                if (reserve && point.Occupant != null && point.Occupant != this) continue;
                candidates.Add(point);
            }
            if (candidates.Count == 0) return null;
            TacticalPoint chosen = candidates[Random.Range(0, candidates.Count)];
            if (reserve) chosen.Occupant = this;
            return chosen;
        }

        void ReleaseGoal()
        {
            if (goal != null && goal.Occupant == (Object)this) goal.Occupant = null;
            goal = null;
            if (State == BotState.Hold) State = BotState.Roam;
        }

        void MoveTo(Vector3 destination)
        {
            if (!agent.isOnNavMesh) return;
            agent.isStopped = false;
            if (!agent.hasPath || (agent.destination - destination).sqrMagnitude > 0.25f) agent.SetDestination(destination);
        }

        void Stop()
        {
            if (agent.isOnNavMesh && agent.hasPath) agent.ResetPath();
        }

        void SetCrouch(bool crouch)
        {
            if (crouching == crouch) return;
            crouching = crouch;
            if (hitboxRig) hitboxRig.SetHeight(crouch ? 1.2f : 1.8f);
        }

        // ------------------------------------------------------------------ Aim

        void UpdateAim(float deltaTime)
        {
            Vector3 direction = DesiredAimDirection();
            float desiredYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            float desiredPitch = -Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;
            bool alert = State == BotState.Engage || State == BotState.Chase || State == BotState.TakeCover;
            float turn = difficulty.turnSpeed * deltaTime * (alert ? 1f : 0.45f);
            aimYaw = Mathf.MoveTowardsAngle(aimYaw, desiredYaw, turn);
            aimPitch = Mathf.MoveTowardsAngle(aimPitch, desiredPitch, turn * 0.7f);
            transform.rotation = Quaternion.Euler(0f, aimYaw, 0f);
        }

        Vector3 DesiredAimDirection()
        {
            Vector3 eye = Eye;
            Vector3 direction = DesiredAimPoint() - eye;
            if (direction.sqrMagnitude < 0.0001f) direction = AimForward;

            if (State == BotState.Engage && target != null)
            {
                // The initial error shrinks as the bot tracks; a little wobble always remains.
                float settle = Mathf.Clamp01((Time.time - targetAcquiredTime) / Mathf.Max(0.01f, difficulty.aimSettleTime));
                var wobble = new Vector2(Mathf.PerlinNoise(wobbleSeed, Time.time * 0.8f) - 0.5f, Mathf.PerlinNoise(Time.time * 0.8f, wobbleSeed) - 0.5f) * (difficulty.aimError * 0.3f);
                Vector2 error = aimErrorAngles * (1f - settle) + wobble;
                direction = Quaternion.AngleAxis(error.x, Vector3.up) * direction;
                Vector3 right = Vector3.Cross(Vector3.up, direction);
                if (right.sqrMagnitude > 0.0001f) direction = Quaternion.AngleAxis(error.y, right.normalized) * direction;
            }
            return direction.normalized;
        }

        Vector3 DesiredAimPoint()
        {
            switch (State)
            {
                case BotState.Engage:
                    return aimForHead ? target.HeadPosition : target.ChestPosition;
                case BotState.Chase:
                case BotState.TakeCover:
                    return lastKnownPosition + Vector3.up * 1.4f;
                case BotState.Investigate:
                    return investigatePosition + Vector3.up * 1.4f;
                case BotState.Hold when goal != null:
                    float scan = Mathf.Sin(Time.time * 0.5f + wobbleSeed) * 25f;
                    return Eye + Quaternion.Euler(0f, goal.FacingYaw + scan, 0f) * Vector3.forward * 10f;
                default:
                    Vector3 velocity = agent.velocity;
                    velocity.y = 0f;
                    if (velocity.sqrMagnitude > 0.25f) return Eye + velocity.normalized * 10f;
                    return Eye + AimForward * 10f;
            }
        }

        // ------------------------------------------------------------------ Reactions

        void OnDamaged(DamageInfo damage)
        {
            if (State == BotState.Dead || damage.Instigator == null) return;
            TeamMember attacker = damage.Instigator.GetComponent<TeamMember>();
            if (attacker == null || attacker.Team == self.Team) return;
            if (target != null && targetVisible) return;

            // Shot by someone unseen: turn to find them.
            AcquireTarget(attacker);
            lastKnownPosition = attacker.transform.position;
            lastSeenTime = Time.time - 0.5f;
        }

        void OnNoise(Vector3 position, float radius, GameObject source)
        {
            if (State == BotState.Dead || source == null || source == gameObject || targetVisible) return;
            TeamMember maker = source.GetComponent<TeamMember>();
            if (maker == null || maker.Team == self.Team) return;
            if (Vector3.Distance(position, transform.position) > Mathf.Min(radius, difficulty.hearingRadius)) return;

            investigatePosition = position;
            investigateUntil = Time.time + 6f;
        }

        void OnDied(DamageInfo damage)
        {
            DiedAt = Time.time;
            ReleaseGoal();
            State = BotState.Dead;
            target = null;
            if (agent.isOnNavMesh) agent.ResetPath();
            agent.enabled = false;
            if (bodyBlocker) bodyBlocker.enabled = false;
            characterAnimator.PlayDeath(damage.Direction);
        }
    }
}
