using Spongykill.Combat;
using Spongykill.Weapons;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Spongykill.Net
{
    /// <summary>A damage event as sent from the server to clients.</summary>
    public struct DamagePacket : INetworkSerializable
    {
        public float Amount;
        public byte Zone;
        public Vector3 Point;
        public Vector3 Direction;
        public ulong InstigatorId;
        public FixedString64Bytes Source;

        public const ulong NoInstigator = ulong.MaxValue;

        public static DamagePacket From(in DamageInfo damage)
        {
            NetworkObject instigator = damage.Instigator ? damage.Instigator.GetComponent<NetworkObject>() : null;
            return new DamagePacket
            {
                Amount = damage.Amount,
                Zone = (byte)damage.Zone,
                Point = damage.Point,
                Direction = damage.Direction,
                InstigatorId = instigator != null && instigator.IsSpawned ? instigator.NetworkObjectId : NoInstigator,
                Source = new FixedString64Bytes(Truncate(damage.Source, 60)),
            };
        }

        public DamageInfo ToInfo()
        {
            GameObject instigator = NetLookup.Find(InstigatorId);
            return new DamageInfo(Amount, (HitZone)Zone, Point, Direction, instigator, Source.ToString());
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Amount);
            serializer.SerializeValue(ref Zone);
            serializer.SerializeValue(ref Point);
            serializer.SerializeValue(ref Direction);
            serializer.SerializeValue(ref InstigatorId);
            serializer.SerializeValue(ref Source);
        }

        public static string Truncate(string text, int length) => string.IsNullOrEmpty(text) ? "" : text.Length <= length ? text : text.Substring(0, length);
    }

    /// <summary>What other players need to draw someone's shot: muzzle, where each pellet ended and what it hit.</summary>
    public struct ShotEffects : INetworkSerializable
    {
        public byte WeaponId;
        public Vector3 Muzzle;
        public Vector3[] Ends;
        public Vector3[] Normals;
        /// <summary>Per pellet: bit 0 = hit something, bit 1 = hit a character.</summary>
        public byte[] Flags;

        public int Count => Ends?.Length ?? 0;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref WeaponId);
            serializer.SerializeValue(ref Muzzle);
            int count = Count;
            serializer.SerializeValue(ref count);
            count = Mathf.Clamp(count, 0, 32);
            if (serializer.IsReader)
            {
                Ends = new Vector3[count];
                Normals = new Vector3[count];
                Flags = new byte[count];
            }
            for (int i = 0; i < count; i++)
            {
                serializer.SerializeValue(ref Ends[i]);
                serializer.SerializeValue(ref Normals[i]);
                serializer.SerializeValue(ref Flags[i]);
            }
        }

        public PelletResult Pellet(int index) => new PelletResult
        {
            EndPoint = Ends[index],
            Normal = Normals[index],
            HitSomething = (Flags[index] & 1) != 0,
            HitCharacter = (Flags[index] & 2) != 0,
        };
    }

    /// <summary>One character hit a client claims, checked by the server before it deals damage.</summary>
    public struct ShotClaim : INetworkSerializable
    {
        public ulong VictimId;
        public byte Zone;
        public float Distance;
        public Vector3 Point;
        public Vector3 Direction;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref VictimId);
            serializer.SerializeValue(ref Zone);
            serializer.SerializeValue(ref Distance);
            serializer.SerializeValue(ref Point);
            serializer.SerializeValue(ref Direction);
        }
    }

    /// <summary>A trigger pull sent by its owner: the effects to show others plus any hits to check.</summary>
    public struct ShotPacket : INetworkSerializable
    {
        public Vector3 Origin;
        public ShotEffects Effects;
        public ShotClaim[] Claims;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Origin);
            serializer.SerializeNetworkSerializable(ref Effects);
            int count = Claims?.Length ?? 0;
            serializer.SerializeValue(ref count);
            count = Mathf.Clamp(count, 0, 32);
            if (serializer.IsReader) Claims = new ShotClaim[count];
            for (int i = 0; i < count; i++) serializer.SerializeNetworkSerializable(ref Claims[i]);
        }
    }

    /// <summary>Finds spawned network objects by id on whichever machine runs the code.</summary>
    public static class NetLookup
    {
        public static GameObject Find(ulong networkObjectId)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (networkObjectId == DamagePacket.NoInstigator || manager == null || manager.SpawnManager == null) return null;
            return manager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject found) ? found.gameObject : null;
        }

        public static T Find<T>(ulong networkObjectId) where T : Component
        {
            GameObject found = Find(networkObjectId);
            return found ? found.GetComponent<T>() : null;
        }

        public static ulong IdOf(Component component)
        {
            if (component == null) return DamagePacket.NoInstigator;
            NetworkObject networkObject = component.GetComponentInParent<NetworkObject>();
            return networkObject != null && networkObject.IsSpawned ? networkObject.NetworkObjectId : DamagePacket.NoInstigator;
        }
    }
}
