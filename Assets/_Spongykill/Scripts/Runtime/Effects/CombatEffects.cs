using Spongykill.Core;
using UnityEngine;

namespace Spongykill.Effects
{
    /// <summary>
    /// Pooled bullet tracers and impact bursts shared by every shooter in the scene. Impacts are
    /// little mesh "bubbles" that float upward, in keeping with the underwater setting.
    /// </summary>
    public class CombatEffects : MonoBehaviour
    {
        [SerializeField] Material tracerMaterial;
        [SerializeField] Material impactMaterial;
        [SerializeField] Mesh impactMesh;
        [SerializeField] int tracerPoolSize = 32;
        [SerializeField] float tracerDuration = 0.07f;
        [SerializeField] float tracerWidth = 0.025f;
        [SerializeField] Color worldImpactColorA = new Color(0.95f, 0.9f, 0.75f);
        [SerializeField] Color worldImpactColorB = new Color(0.75f, 0.92f, 1f);
        [SerializeField] Color characterImpactColorA = new Color(1f, 0.45f, 0.4f);
        [SerializeField] Color characterImpactColorB = new Color(1f, 0.8f, 0.3f);

        static CombatEffects instance;

        LineRenderer[] tracers;
        float[] tracerStartTimes;
        int nextTracer;
        ParticleSystem impacts;

        /// <summary>The scene's effects, or null when none is present (effects are then skipped).</summary>
        public static CombatEffects Instance => instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        void Awake()
        {
            instance = this;
            CreateTracers();
            impacts = CreateImpactSystem();
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        public void Tracer(Vector3 from, Vector3 to, Color color)
        {
            if (!GameSettings.Current.effects) return;
            LineRenderer tracer = tracers[nextTracer];
            tracerStartTimes[nextTracer] = Time.time;
            nextTracer = (nextTracer + 1) % tracers.Length;

            tracer.SetPosition(0, from);
            tracer.SetPosition(1, to);
            tracer.startColor = color;
            tracer.endColor = color;
            tracer.widthMultiplier = tracerWidth;
            tracer.enabled = true;
        }

        public void Impact(Vector3 point, Vector3 normal, bool character)
        {
            if (!GameSettings.Current.effects) return;
            int count = character ? 10 : 7;
            Color colorA = character ? characterImpactColorA : worldImpactColorA;
            Color colorB = character ? characterImpactColorB : worldImpactColorB;
            for (int i = 0; i < count; i++)
            {
                Vector3 velocity = (normal + Random.insideUnitSphere * 0.8f).normalized * Random.Range(1f, 3.2f);
                var emit = new ParticleSystem.EmitParams
                {
                    position = point + normal * 0.03f,
                    velocity = velocity,
                    startSize = Random.Range(0.035f, character ? 0.11f : 0.08f),
                    startLifetime = Random.Range(0.35f, 0.8f),
                    startColor = Color.Lerp(colorA, colorB, Random.value),
                };
                impacts.Emit(emit, 1);
            }
        }

        void Update()
        {
            for (int i = 0; i < tracers.Length; i++)
            {
                if (!tracers[i].enabled) continue;
                float age = (Time.time - tracerStartTimes[i]) / tracerDuration;
                if (age >= 1f) tracers[i].enabled = false;
                else tracers[i].widthMultiplier = tracerWidth * (1f - age);
            }
        }

        void CreateTracers()
        {
            tracers = new LineRenderer[tracerPoolSize];
            tracerStartTimes = new float[tracerPoolSize];
            for (int i = 0; i < tracerPoolSize; i++)
            {
                var tracerObject = new GameObject($"Tracer_{i:00}");
                tracerObject.transform.SetParent(transform, false);
                LineRenderer tracer = tracerObject.AddComponent<LineRenderer>();
                tracer.sharedMaterial = tracerMaterial;
                tracer.positionCount = 2;
                tracer.useWorldSpace = true;
                tracer.numCapVertices = 2;
                tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tracer.receiveShadows = false;
                tracer.enabled = false;
                tracers[i] = tracer;
            }
        }

        ParticleSystem CreateImpactSystem()
        {
            var systemObject = new GameObject("Impacts");
            systemObject.transform.SetParent(transform, false);
            ParticleSystem system = systemObject.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.maxParticles = 600;
            // Negative gravity: debris drifts upward like bubbles.
            main.gravityModifier = -0.12f;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;

            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

            ParticleSystem.LimitVelocityOverLifetimeModule drag = system.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 3f;

            var particleRenderer = systemObject.GetComponent<ParticleSystemRenderer>();
            particleRenderer.renderMode = ParticleSystemRenderMode.Mesh;
            particleRenderer.mesh = impactMesh;
            particleRenderer.sharedMaterial = impactMaterial;
            particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            system.Play();
            return system;
        }
    }
}
