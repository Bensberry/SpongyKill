using UnityEngine;

namespace Spongykill.Weapons
{
    /// <summary>
    /// Flips the balisong's blade and free handle: it flips open while drawing, and the inspect runs
    /// four quick flips, a double twirl in the hand and a last flip-and-catch.
    /// Blade angle 0 = open (blade forward), 180 = closed (blade tucked between the handles).
    /// </summary>
    public class BalisongAnimator : MonoBehaviour, IViewmodelAnimator
    {
        [SerializeField] Transform blade;
        [Tooltip("The free (bite) handle; it swings all the way round on every flip.")]
        [SerializeField] Transform biteHandle;
        [Tooltip("Everything but the hand, for twirls around the knife's long axis.")]
        [SerializeField] Transform spin;

        // Inspect timeline (0..1): when each flip starts. Flips alternate closing and opening.
        static readonly float[] FlipStarts = { 0.04f, 0.13f, 0.22f, 0.31f, 0.76f, 0.85f };
        const float FlipLength = 0.09f;

        public void Animate(in ViewmodelState state, float deltaTime)
        {
            float bladeAngle = 0f;
            float biteAngle = 0f;
            float twirl = 0f;

            if (state.DrawProgress < 1f)
            {
                // Comes out closed and flips open with a roll.
                float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.9f, state.DrawProgress));
                bladeAngle = 180f * (1f - t);
                biteAngle = 360f * t;
                twirl = 360f * t;
            }
            else if (state.InspectProgress >= 0f)
            {
                float progress = state.InspectProgress;
                for (int i = 0; i < FlipStarts.Length; i++)
                {
                    float flip = Mathf.InverseLerp(FlipStarts[i], FlipStarts[i] + FlipLength, progress);
                    if (flip <= 0f) break;
                    float eased = Mathf.SmoothStep(0f, 1f, flip);
                    bladeAngle = i % 2 == 0 ? 180f * eased : 180f * (1f - eased);
                    biteAngle += 360f * eased;
                }
                twirl = 720f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.7f, progress));
            }

            if (blade) blade.localRotation = Quaternion.Euler(bladeAngle, 0f, 0f);
            if (biteHandle) biteHandle.localRotation = Quaternion.Euler(biteAngle, 0f, 0f);
            if (spin) spin.localRotation = Quaternion.Euler(0f, 0f, twirl);
        }
    }
}
