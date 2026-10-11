using System.Collections;
using UnityEngine;

namespace Spongykill.Characters
{
    /// <summary>
    /// Procedural animation for the primitive operator body: leg swing and bob while moving, crouching,
    /// aiming the weapon up and down, and a topple on death. Placeholder until rigged models arrive.
    /// </summary>
    public class CharacterAnimator : MonoBehaviour
    {
        [SerializeField] Transform visualRoot;
        [Tooltip("Hips-up group, lowered when crouching.")]
        [SerializeField] Transform upperBody;
        [Tooltip("Pitches with the aim; the weapon and arms hang off it.")]
        [SerializeField] Transform aimPivot;
        [SerializeField] Transform leftLeg;
        [SerializeField] Transform rightLeg;
        [SerializeField] float crouchDrop = 0.45f;

        Vector3 upperBodyBase;
        Vector3 legScale = Vector3.one;
        float stridePhase;
        float crouchBlend;
        Coroutine death;

        void Awake()
        {
            if (upperBody) upperBodyBase = upperBody.localPosition;
            if (leftLeg) legScale = leftLeg.localScale;
        }

        public void Tick(Vector3 velocity, float aimPitch, bool crouching, float deltaTime)
        {
            float speed = new Vector2(velocity.x, velocity.z).magnitude;
            float moving = Mathf.Clamp01(speed / 4f);
            stridePhase += speed * deltaTime * 2.4f;
            crouchBlend = Mathf.MoveTowards(crouchBlend, crouching ? 1f : 0f, deltaTime * 6f);

            float swing = Mathf.Sin(stridePhase) * 35f * moving;
            var legSize = new Vector3(legScale.x, legScale.y * Mathf.Lerp(1f, 0.5f, crouchBlend), legScale.z);
            if (leftLeg)
            {
                leftLeg.localRotation = Quaternion.Euler(swing, 0f, 0f);
                leftLeg.localScale = legSize;
            }
            if (rightLeg)
            {
                rightLeg.localRotation = Quaternion.Euler(-swing, 0f, 0f);
                rightLeg.localScale = legSize;
            }

            if (upperBody)
            {
                float bob = Mathf.Abs(Mathf.Sin(stridePhase)) * 0.05f * moving;
                upperBody.localPosition = upperBodyBase + Vector3.up * (bob - crouchDrop * crouchBlend);
            }
            if (aimPivot) aimPivot.localRotation = Quaternion.Euler(Mathf.Clamp(aimPitch, -70f, 70f), 0f, 0f);
        }

        public void PlayDeath(Vector3 hitDirection)
        {
            if (death != null) StopCoroutine(death);
            death = StartCoroutine(Topple(hitDirection));
        }

        public void ResetPose()
        {
            if (death != null) StopCoroutine(death);
            death = null;
            if (visualRoot) visualRoot.localRotation = Quaternion.identity;
            crouchBlend = 0f;
        }

        IEnumerator Topple(Vector3 hitDirection)
        {
            if (visualRoot == null) yield break;
            Vector3 away = hitDirection;
            away.y = 0f;
            if (away.sqrMagnitude < 0.001f) away = -transform.forward;
            Vector3 axis = Vector3.Cross(Vector3.up, away.normalized);
            Quaternion upright = visualRoot.rotation;
            Quaternion fallen = Quaternion.AngleAxis(85f, axis) * upright;
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.35f)
            {
                visualRoot.rotation = Quaternion.Slerp(upright, fallen, t * t);
                yield return null;
            }
            visualRoot.rotation = fallen;
        }
    }
}
