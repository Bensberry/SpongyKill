using UnityEngine;

namespace Spongykill.Maps
{
    /// <summary>A bomb plant zone: an axis-aligned box around the transform (the box bottom sits on the floor).</summary>
    public class BombSite : MonoBehaviour
    {
        [SerializeField] string siteName = "A";
        [SerializeField] Vector3 size = new Vector3(12f, 3f, 12f);

        public string SiteName => siteName;
        public Vector3 Size => size;
        public Vector3 Center => transform.position + Vector3.up * (size.y * 0.5f);

        public void Configure(string newName, Vector3 newSize)
        {
            siteName = newName;
            size = newSize;
        }

        public bool Contains(Vector3 point)
        {
            Vector3 local = point - Center;
            return Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.y) <= size.y * 0.5f && Mathf.Abs(local.z) <= size.z * 0.5f;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.25f);
            Gizmos.DrawCube(Center, size);
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.9f);
            Gizmos.DrawWireCube(Center, size);
        }
    }
}
