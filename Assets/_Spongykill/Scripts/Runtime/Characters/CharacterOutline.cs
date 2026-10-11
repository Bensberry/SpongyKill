using Spongykill.Combat;
using Spongykill.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Spongykill.Characters
{
    /// <summary>
    /// A coloured outline around enemies of the local player so they don't blend into the colourful
    /// map. Each body part gets an outline shell (see the CharacterOutline shader) that follows its
    /// animation. Shown only while the character is alive and an enemy, and only when the outline
    /// setting is on; colour and thickness come from the settings too.
    /// </summary>
    public class CharacterOutline : MonoBehaviour
    {
        [SerializeField] Renderer[] parts = new Renderer[0];
        [SerializeField] Material outlineMaterial;
        [SerializeField] TeamMember member;

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int WidthId = Shader.PropertyToID("_Width");
        static readonly int ExtentsId = Shader.PropertyToID("_Extents");
        static readonly int CenterId = Shader.PropertyToID("_Center");

        Renderer[] shells;
        MaterialPropertyBlock block;
        bool shown;
        bool styleDirty = true;

        void Awake()
        {
            if (member == null) member = GetComponentInParent<TeamMember>();
            BuildShells();
            SetShown(false);
        }

        void OnEnable() => GameSettings.Changed += MarkStyleDirty;

        void OnDisable() => GameSettings.Changed -= MarkStyleDirty;

        void MarkStyleDirty() => styleDirty = true;

        void BuildShells()
        {
            if (outlineMaterial == null || Application.isBatchMode)
            {
                shells = new Renderer[0];
                return;
            }
            block = new MaterialPropertyBlock();
            shells = new Renderer[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                Renderer part = parts[i];
                if (part == null || !part.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;
                var shell = new GameObject("Outline");
                shell.layer = part.gameObject.layer;
                shell.transform.SetParent(part.transform, false);
                shell.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var shellRenderer = shell.AddComponent<MeshRenderer>();
                shellRenderer.sharedMaterial = outlineMaterial;
                shellRenderer.shadowCastingMode = ShadowCastingMode.Off;
                shellRenderer.receiveShadows = false;
                shellRenderer.lightProbeUsage = LightProbeUsage.Off;
                shellRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                shells[i] = shellRenderer;
            }
        }

        void LateUpdate()
        {
            TeamMember local = TeamMember.LocalPlayer;
            bool wanted = GameSettings.Current.enemyOutline && member != null && member.IsAlive
                && local != null && local != member && local.Team != member.Team;
            if (wanted != shown) SetShown(wanted);
            if (shown && styleDirty) ApplyStyle();
        }

        void SetShown(bool value)
        {
            shown = value;
            foreach (Renderer shell in shells)
                if (shell) shell.enabled = value;
            if (value) styleDirty = true;
        }

        void ApplyStyle()
        {
            styleDirty = false;
            GameSettings settings = GameSettings.Current;
            for (int i = 0; i < shells.Length; i++)
            {
                if (shells[i] == null) continue;
                Bounds bounds = shells[i].GetComponent<MeshFilter>().sharedMesh.bounds;
                block.Clear();
                block.SetColor(ColorId, settings.EnemyOutlineColor);
                block.SetFloat(WidthId, settings.enemyOutlineWidth);
                block.SetVector(ExtentsId, bounds.extents);
                block.SetVector(CenterId, bounds.center);
                shells[i].SetPropertyBlock(block);
            }
        }

        public void Configure(Renderer[] outlinedParts, Material material, TeamMember owner)
        {
            parts = outlinedParts;
            outlineMaterial = material;
            member = owner;
        }
    }
}
