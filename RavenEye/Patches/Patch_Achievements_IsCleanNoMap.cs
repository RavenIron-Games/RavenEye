using System;
using HarmonyLib;
using RavenIron.RavenEye.Core;

namespace RavenIron.RavenEye.Patches
{
    /// <summary>
    /// Keeps the vanilla "no map" achievement honest for a player RavenEye has given the map.
    ///
    /// Decompile-verified on Valheim 1.0.15: `Achievements.IsCleanNoMap()` (public static)
    /// is just `ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoMap)`, and its only callers are
    /// the four world-edge checks in `Player` that add to `ExploreNorthNoMap`,
    /// `ExploreSouthNoMap`, `ExploreEastNoMap` and `ExploreWestNoMap`. RavenEye lifts
    /// `Game.m_noMap` but leaves the world key set, so without this a granted admin or host
    /// could sail the world's edges with the map up and still be credited as map-less.
    ///
    /// Result-decorating postfix at default priority (house rule 1): vanilla decides, and
    /// the only change is "not clean" while the grant is active. It never turns a map world
    /// into a clean no-map one.
    /// </summary>
    [HarmonyPatch(typeof(Achievements), nameof(Achievements.IsCleanNoMap))]
    public static class Patch_Achievements_IsCleanNoMap
    {
        private static int _warned;

        private static void Postfix(ref bool __result)
        {
            try
            {
                if (!__result) return;
                __result = Grant.CleanNoMap(__result, RavenEyeTick.EvaluateGrant(ZNet.instance));
            }
            catch (Exception ex)
            {
                // Fail toward NOT crediting: a throw here means we could not tell whether the
                // map was granted, and a wrongly earned achievement cannot be taken back.
                __result = false;
                if (_warned++ < 3)
                    RavenEye.Log.LogWarning($"IsCleanNoMap postfix threw ({ex.GetType().Name}: {ex.Message}); no-map credit withheld.");
            }
        }
    }
}
