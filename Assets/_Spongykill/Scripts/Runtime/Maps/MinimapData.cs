using UnityEngine;

namespace Spongykill.Maps
{
    /// <summary>A map's top-down minimap image and the world area it covers (X right, Z up).</summary>
    public class MinimapData : MonoBehaviour
    {
        [SerializeField] Texture2D texture;
        [Tooltip("World X/Z of the image's bottom-left corner.")]
        [SerializeField] Vector2 worldMin;
        [Tooltip("World size in metres the image covers.")]
        [SerializeField] Vector2 worldSize = new Vector2(100f, 100f);

        public Texture2D Texture => texture;
        public Vector2 WorldMin => worldMin;
        public Vector2 WorldSize => worldSize;

        public void Configure(Texture2D image, Vector2 min, Vector2 size)
        {
            texture = image;
            worldMin = min;
            worldSize = size;
        }
    }
}
