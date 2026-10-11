using System.Collections.Generic;
using Spongykill.Combat;
using Spongykill.Core;
using Spongykill.Maps;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Spongykill.UI
{
    /// <summary>
    /// Top-left radar: the baked map image around you (north up, or turning with you), teammates as
    /// team-coloured dots, enemies in red only while spotted (in your view, or for a moment after they
    /// fire), and the bomb-site letters as landmarks.
    /// </summary>
    public class Minimap : MonoBehaviour
    {
        const float Size = 290f;
        const float VisibleRadius = 36f;
        const float EdgeMargin = 10f;
        const float ShotRevealSeconds = 2f;
        const float SightRevealSeconds = 0.6f;
        const float SightRange = 70f;

        MinimapData data;
        MapDefinition map;
        RectTransform rotator;
        RectTransform mapImage;
        RectTransform iconLayer;
        RectTransform self;
        readonly List<(BombSite site, RectTransform label)> siteLabels = new List<(BombSite, RectTransform)>();
        readonly Dictionary<TeamMember, Image> icons = new Dictionary<TeamMember, Image>();
        readonly Dictionary<TeamMember, float> spottedUntil = new Dictionary<TeamMember, float>();
        readonly List<TeamMember> stale = new List<TeamMember>();
        float nextSightCheck;
        Sprite arrow;

        /// <summary>Adds the minimap to a HUD canvas when the scene has map data.</summary>
        public static Minimap Create(RectTransform parent)
        {
            var mapData = FindAnyObjectByType<MinimapData>();
            if (mapData == null || mapData.Texture == null) return null;
            RectTransform root = UiKit.Place(UiKit.Rect(parent, "Minimap"), new Vector2(0f, 1f), new Vector2(22f, -22f), new Vector2(Size + 12f, Size + 12f));
            var minimap = root.gameObject.AddComponent<Minimap>();
            minimap.data = mapData;
            minimap.map = FindAnyObjectByType<MapDefinition>();
            minimap.Build(root);
            return minimap;
        }

        void OnEnable() => CombatEvents.ShotFired += OnShotFired;

        void OnDisable() => CombatEvents.ShotFired -= OnShotFired;

        void Build(RectTransform root)
        {
            Image frame = UiKit.Image(root, "Frame", new Color(0.02f, 0.1f, 0.16f, 0.85f), UiKit.Rounded);
            UiKit.Stretch(frame.rectTransform);
            Image maskImage = UiKit.Image(root, "Mask", Color.white, UiKit.Rounded);
            UiKit.Stretch(maskImage.rectTransform, 6f);
            maskImage.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            RectTransform viewport = maskImage.rectTransform;

            rotator = UiKit.Place(UiKit.Rect(viewport, "Rotator"), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rotator.pivot = new Vector2(0.5f, 0.5f);
            var raw = UiKit.Rect(rotator, "Map").gameObject.AddComponent<RawImage>();
            raw.texture = data.Texture;
            raw.raycastTarget = false;
            mapImage = raw.rectTransform;
            mapImage.anchorMin = mapImage.anchorMax = new Vector2(0.5f, 0.5f);
            mapImage.pivot = Vector2.zero;

            if (map != null)
            {
                foreach (BombSite site in map.BombSites)
                {
                    if (site == null) continue;
                    TextMeshProUGUI label = UiKit.Label(rotator, $"Site{site.SiteName}", site.SiteName, 34f, TextAlignmentOptions.Center, new Color(1f, 0.55f, 0.45f), FontStyles.Bold);
                    label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                    label.rectTransform.sizeDelta = new Vector2(60f, 50f);
                    siteLabels.Add((site, label.rectTransform));
                }
            }

            iconLayer = UiKit.Place(UiKit.Rect(viewport, "Icons"), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            iconLayer.pivot = new Vector2(0.5f, 0.5f);
            arrow = CreateArrowSprite();
            Image selfImage = UiKit.Image(iconLayer, "Self", Color.white, arrow);
            selfImage.rectTransform.anchorMin = selfImage.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            selfImage.rectTransform.sizeDelta = new Vector2(26f, 26f);
            self = selfImage.rectTransform;
        }

        void LateUpdate()
        {
            GameSettings settings = GameSettings.Current;
            TeamMember local = TeamMember.LocalPlayer;
            Vector2 center = local != null ? Flat(local.transform.position) : data.WorldMin + data.WorldSize * 0.5f;
            float yaw = local != null ? local.transform.eulerAngles.y : 0f;
            bool rotate = settings.minimapRotate && local != null;
            float metres = VisibleRadius / Mathf.Clamp(settings.minimapZoom, 0.5f, 2f);
            float scale = Size * 0.5f / metres;

            rotator.localRotation = Quaternion.Euler(0f, 0f, rotate ? yaw : 0f);
            mapImage.sizeDelta = data.WorldSize * scale;
            mapImage.anchoredPosition = (data.WorldMin - center) * scale;
            foreach ((BombSite site, RectTransform label) in siteLabels)
            {
                label.anchoredPosition = (Flat(site.transform.position) - center) * scale;
                label.localRotation = Quaternion.Euler(0f, 0f, rotate ? -yaw : 0f);
            }

            self.gameObject.SetActive(local != null && local.IsAlive);
            self.localRotation = Quaternion.Euler(0f, 0f, rotate ? 0f : -yaw);
            if (local != null) self.GetComponent<Image>().color = TeamColors.For(local.Team);

            UpdateSpotting(local);
            UpdateIcons(local, center, rotate ? yaw : 0f, scale);
        }

        void UpdateIcons(TeamMember local, Vector2 center, float rotation, float scale)
        {
            float radius = Size * 0.5f - EdgeMargin;
            Quaternion turn = Quaternion.Euler(0f, 0f, rotation);
            foreach (TeamMember member in TeamMember.All)
            {
                if (member == local) continue;
                if (!icons.TryGetValue(member, out Image icon))
                {
                    icon = UiKit.Image(iconLayer, member.DisplayName, Color.white, UiKit.Circle);
                    icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                    icons[member] = icon;
                }

                bool ally = local == null || member.Team == local.Team;
                bool visible = member.IsAlive && (ally || IsSpotted(member));
                Vector2 offset = turn * ((Flat(member.transform.position) - center) * scale);
                if (offset.magnitude > radius)
                {
                    // Teammates stay pinned to the edge so you always know which way they are.
                    if (ally) offset = offset.normalized * radius;
                    else visible = false;
                }
                icon.gameObject.SetActive(visible);
                if (!visible) continue;
                icon.rectTransform.anchoredPosition = offset;
                icon.rectTransform.sizeDelta = ally ? new Vector2(16f, 16f) : new Vector2(18f, 18f);
                icon.color = ally ? TeamColors.For(member.Team) : new Color(1f, 0.2f, 0.2f);
            }

            // Forget people who left.
            stale.Clear();
            foreach (TeamMember member in icons.Keys)
                if (member == null || !member.isActiveAndEnabled) stale.Add(member);
            foreach (TeamMember member in stale)
            {
                if (icons[member]) Destroy(icons[member].gameObject);
                icons.Remove(member);
                spottedUntil.Remove(member);
            }
        }

        /// <summary>Enemies inside your view with a clear line of sight show up briefly.</summary>
        void UpdateSpotting(TeamMember local)
        {
            if (local == null || !local.IsAlive || Time.time < nextSightCheck) return;
            nextSightCheck = Time.time + 0.2f;
            Camera view = Camera.main;
            if (view == null) return;
            Vector3 eye = view.transform.position;
            int mask = GameLayers.DefaultMaskExcluding(GameLayers.Player, GameLayers.Bot, GameLayers.Hitbox, GameLayers.Viewmodel);
            foreach (TeamMember member in TeamMember.All)
            {
                if (member.Team == local.Team || !member.IsAlive) continue;
                Vector3 toTarget = member.ChestPosition - eye;
                float distance = toTarget.magnitude;
                if (distance > SightRange) continue;
                Vector3 viewport = view.WorldToViewportPoint(member.ChestPosition);
                if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f) continue;
                if (Physics.Raycast(eye, toTarget / distance, distance - 0.3f, mask, QueryTriggerInteraction.Ignore)) continue;
                Spot(member, SightRevealSeconds);
            }
        }

        void OnShotFired(GameObject shooter)
        {
            if (shooter == null) return;
            TeamMember member = shooter.GetComponentInParent<TeamMember>();
            TeamMember local = TeamMember.LocalPlayer;
            if (member != null && local != null && member.Team != local.Team) Spot(member, ShotRevealSeconds);
        }

        void Spot(TeamMember member, float seconds)
        {
            float until = Time.time + seconds;
            if (!spottedUntil.TryGetValue(member, out float current) || current < until) spottedUntil[member] = until;
        }

        bool IsSpotted(TeamMember member) => spottedUntil.TryGetValue(member, out float until) && Time.time < until;

        static Vector2 Flat(Vector3 position) => new Vector2(position.x, position.z);

        /// <summary>A white arrowhead pointing up.</summary>
        static Sprite CreateArrowSprite()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size - 0.5f;
                    float v = (y + 0.5f) / size;
                    // Inside a triangle from the tip (top centre) to the bottom corners, with a notch.
                    float halfWidth = (1f - v) * 0.45f;
                    bool inside = v > 0.06f && Mathf.Abs(u) < halfWidth && v > 0.06f + Mathf.Abs(u) * 0.55f;
                    pixels[y * size + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
    }
}
