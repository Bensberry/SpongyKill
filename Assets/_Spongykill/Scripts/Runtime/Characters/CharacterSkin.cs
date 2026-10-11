using Spongykill.Core;
using UnityEngine;

namespace Spongykill.Characters
{
    /// <summary>A cosmetic look for the shared operator body: creature skin, team outfit and accents.</summary>
    [CreateAssetMenu(menuName = "Spongykill/Character Skin", fileName = "SK_NewSkin")]
    public class CharacterSkin : ScriptableObject
    {
        public string displayName = "Skin";
        public Team team;
        [Tooltip("Body, head, arms and legs.")]
        public Material skin;
        [Tooltip("Vest and helmet.")]
        public Material outfit;
        [Tooltip("Belt, boots and gloves.")]
        public Material accent;
    }
}
