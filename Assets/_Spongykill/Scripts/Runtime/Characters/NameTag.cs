using Spongykill.Combat;
using UnityEngine;

namespace Spongykill.Characters
{
    /// <summary>Floating name above a teammate's head; hidden for enemies and the dead.</summary>
    public class NameTag : MonoBehaviour
    {
        [SerializeField] TextMesh label;
        [SerializeField] TeamMember owner;

        Renderer labelRenderer;

        void Awake()
        {
            if (label) labelRenderer = label.GetComponent<Renderer>();
        }

        void LateUpdate()
        {
            if (label == null || owner == null) return;
            TeamMember localPlayer = TeamMember.LocalPlayer;
            bool show = owner.IsAlive && localPlayer != null && localPlayer != owner && localPlayer.Team == owner.Team;
            if (labelRenderer.enabled != show) labelRenderer.enabled = show;
            if (!show) return;

            if (label.text != owner.DisplayName) label.text = owner.DisplayName;
            label.color = Core.TeamColors.For(owner.Team);
            Camera view = Camera.main;
            // Face the camera; TextMesh reads correctly when its forward points away from the viewer.
            if (view) label.transform.rotation = Quaternion.LookRotation(label.transform.position - view.transform.position);
        }
    }
}
