using UnityEngine;

namespace Spongykill.Core
{
    /// <summary>Single place that captures and releases the mouse cursor.</summary>
    public static class CursorLock
    {
        public static bool IsLocked => Cursor.lockState == CursorLockMode.Locked;

        public static void Lock()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        public static void Unlock()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
