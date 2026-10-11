using System.Collections.Generic;
using Spongykill.Combat;
using Spongykill.Core;
using TMPro;
using UnityEngine;

namespace Spongykill.UI
{
    /// <summary>Top-right list of recent eliminations, coloured by team, with the local player's in bold.</summary>
    public class KillFeed : MonoBehaviour
    {
        [SerializeField] TMP_Text[] rows = new TMP_Text[0];
        [SerializeField] float entryLifetime = 6f;

        readonly List<(string text, float time)> entries = new List<(string, float)>();

        void OnEnable() => CombatEvents.Eliminated += OnEliminated;

        void OnDisable() => CombatEvents.Eliminated -= OnEliminated;

        void Start() => Refresh();

        void Update()
        {
            int before = entries.Count;
            entries.RemoveAll(entry => Time.time - entry.time > entryLifetime);
            if (entries.Count != before) Refresh();

            // Fade each row over its last second.
            for (int i = 0; i < rows.Length && i < entries.Count; i++)
                rows[i].alpha = Mathf.Clamp01(entryLifetime - (Time.time - entries[i].time));
        }

        void OnEliminated(EliminationRecord record)
        {
            if (record.Victim == null) return;
            string victim = Name(record.Victim);
            string weapon = string.IsNullOrEmpty(record.Source) ? "?" : record.Source;
            string line = record.Killer != null && record.Killer != record.Victim
                ? $"{Name(record.Killer)}  <color=#FFFFFFAA>[{weapon}{(record.Headshot ? " · head" : "")}]</color>  {victim}"
                : $"<color=#FFFFFFAA>[{weapon}]</color>  {victim}";

            entries.Insert(0, (line, Time.time));
            if (entries.Count > rows.Length) entries.RemoveAt(entries.Count - 1);
            Refresh();
        }

        static string Name(TeamMember member)
        {
            string color = ColorUtility.ToHtmlStringRGB(TeamColors.For(member.Team));
            string name = member.IsLocalPlayer ? $"<b>{member.DisplayName}</b>" : member.DisplayName;
            return $"<color=#{color}>{name}</color>";
        }

        void Refresh()
        {
            for (int i = 0; i < rows.Length; i++)
            {
                bool used = i < entries.Count;
                rows[i].gameObject.SetActive(used);
                if (used) rows[i].SetText(entries[i].text);
            }
        }
    }
}
