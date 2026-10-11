using Spongykill.Characters;
using Spongykill.Core;
using Spongykill.Weapons;
using UnityEngine;

namespace Spongykill.Net
{
    /// <summary>
    /// Every weapon and skin with a stable index, so the network can send a small number instead of an
    /// asset. Lives in Resources so dedicated servers and clients load the same list.
    /// </summary>
    [CreateAssetMenu(menuName = "Spongykill/Game Catalog", fileName = "GameCatalog")]
    public class GameCatalog : ScriptableObject
    {
        public const string ResourcePath = "GameCatalog";
        public const byte None = byte.MaxValue;

        [SerializeField] WeaponDefinition[] weapons = new WeaponDefinition[0];
        [SerializeField] CharacterSkin[] attackerSkins = new CharacterSkin[0];
        [SerializeField] CharacterSkin[] defenderSkins = new CharacterSkin[0];

        static GameCatalog instance;

        public static GameCatalog Instance => instance ? instance : instance = Resources.Load<GameCatalog>(ResourcePath);

        public int WeaponCount => weapons.Length;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        public byte WeaponId(WeaponDefinition weapon)
        {
            if (weapon == null) return None;
            for (int i = 0; i < weapons.Length; i++)
                if (weapons[i] == weapon) return (byte)i;
            return None;
        }

        public WeaponDefinition Weapon(byte id) => id < weapons.Length ? weapons[id] : null;

        public CharacterSkin[] SkinsFor(Team team) => team == Team.Attackers ? attackerSkins : defenderSkins;

        public CharacterSkin Skin(Team team, int index)
        {
            CharacterSkin[] skins = SkinsFor(team);
            return skins.Length == 0 ? null : skins[Mathf.Abs(index) % skins.Length];
        }

        public void Configure(WeaponDefinition[] allWeapons, CharacterSkin[] attackers, CharacterSkin[] defenders)
        {
            weapons = allWeapons;
            attackerSkins = attackers;
            defenderSkins = defenders;
        }
    }
}
