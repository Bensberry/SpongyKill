using System;
using System.Collections.Generic;
using Spongykill.Combat;
using Spongykill.Controls;
using Spongykill.Core;
using UnityEngine;

namespace Spongykill.UI
{
    /// <summary>
    /// Which full-screen menus are open. Opening one blocks gameplay input, frees the mouse and lifts
    /// any held touch buttons; closing the last one recaptures the mouse if the player is alive.
    /// </summary>
    public static class MenuState
    {
        static readonly List<object> Open = new List<object>();

        public static event Action Changed;

        public static bool AnyOpen => Open.Count > 0;

        public static bool IsOpen(object menu) => Open.Contains(menu);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Open.Clear();
            Changed = null;
        }

        public static void Push(object menu)
        {
            if (Open.Contains(menu)) return;
            Open.Add(menu);
            TouchInput.ReleaseAll();
            Refresh();
        }

        public static void Pop(object menu)
        {
            if (!Open.Remove(menu)) return;
            Refresh();
        }

        static void Refresh()
        {
            InputGate.MenuOpen = Open.Count > 0;
            if (Open.Count > 0) CursorLock.Unlock();
            else if (!ControlScheme.UseTouch && LocalPlayerAlive) CursorLock.Lock();
            Changed?.Invoke();
        }

        static bool LocalPlayerAlive => TeamMember.LocalPlayer != null && TeamMember.LocalPlayer.IsAlive;
    }
}
