using System;
using HarmonyLib;
using RavenIron.RavenEye.Core;

namespace RavenIron.RavenEye.Patches
{
    /// <summary>
    /// The unlock. All of it.
    ///
    /// Decompile-verified 2026-09-06 (0.221.12): `Game.m_noMap` is read in exactly ONE place
    /// in the game — `Minimap.SetMapMode`, which coerces any requested mode to None while it
    /// is set — and `Minimap.Update` asks for Small every frame the mode is None. So the flag
    /// IS the lock, and `Game.UpdateNoMap` is the only writer that matters: it sets the flag
    /// from two inputs, the world's NoMap key OR the character's own `nomap` opt-out
    /// (`PlatformPrefs "mapenabled_<name>"`), then applies it.
    ///
    /// This postfix relaxes ONE of those inputs for a granted admin: the world's rule. The
    /// character's own opt-out still stands — an admin who typed `nomap` to hide their own
    /// map keeps it hidden. Everything else is vanilla: exploration, pins, the large map, the
    /// share-position toggle, all of it drawn by the game's own code.
    ///
    /// Decorating postfix at default priority (amended house rule 1): vanilla has already
    /// decided and we adjust what survives. Any mod that writes the flag after us wins, and
    /// vanilla itself re-runs this method on every global-key resync and every first spawn,
    /// so a granted admin cannot be silently relocked by a resync, and a revoked one is
    /// relocked by vanilla's own arithmetic the moment the grant tracker re-runs it.
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.UpdateNoMap))]
    public static class Patch_Game_UpdateNoMap
    {
        private static int _warned;

        private static void Postfix()
        {
            try
            {
                ZNet znet = ZNet.instance;
                if (!RavenEyeTick.EvaluateGrant(znet)) return;

                Minimap map = Minimap.instance;
                if (map == null) return;

                bool personalOptOut = false;
                Player local = Player.m_localPlayer;
                if (local != null)
                    personalOptOut = PlatformPrefs.GetFloat("mapenabled_" + local.GetPlayerName(), 1f) == 0f;

                Game.m_noMap = personalOptOut;
                map.SetMapMode(personalOptOut ? Minimap.MapMode.None : Minimap.MapMode.Small);
            }
            catch (Exception ex)
            {
                if (_warned++ < 3)
                    RavenEye.Log.LogWarning($"UpdateNoMap postfix threw ({ex.GetType().Name}: {ex.Message}); vanilla's decision stands.");
            }
        }
    }
}
