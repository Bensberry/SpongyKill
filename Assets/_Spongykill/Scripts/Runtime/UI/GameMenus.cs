using UnityEngine;

namespace Spongykill.UI
{
    /// <summary>Menu actions shared by several screens. The pause and settings menus will live here too.</summary>
    public static class GameMenus
    {
        public static void QuitGame()
        {
            Time.timeScale = 1f;
            // Ignored inside the Editor; stop Play Mode there instead.
            Application.Quit();
        }
    }
}
