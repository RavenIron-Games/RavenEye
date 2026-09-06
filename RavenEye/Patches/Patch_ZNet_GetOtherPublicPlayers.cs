using System;
using System.Collections.Generic;
using HarmonyLib;
using RavenIron.RavenEye.Core;
using RavenIron.RavenEye.Net;

namespace RavenIron.RavenEye.Patches
{
    /// <summary>
    /// The pins.
    ///
    /// `Minimap.UpdatePlayerPins` (every frame) clears a list, hands it to
    /// `ZNet.GetOtherPublicPlayers`, and draws a vanilla player pin — icon and name — for
    /// every entry that comes back. Vanilla fills it with the players who share position.
    /// This appends the rest, from the server's roster, in the server's order. Nothing is
    /// drawn by this mod; the game draws what it always draws, for more people.
    ///
    /// The canonical amended-rule-1 postfix: appending to a result is the whole point, and
    /// it cedes every fight — anything that rewrites the list after us wins.
    ///
    /// Vanilla matches pins to entries by index and smooths only when the name matches, so
    /// a player who flips their share toggle migrates from our tail to vanilla's head and
    /// their pin snaps rather than glides; two characters with one name defeat the smoothing
    /// entirely. Both already happen in vanilla for public players.
    /// </summary>
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.GetOtherPublicPlayers))]
    public static class Patch_ZNet_GetOtherPublicPlayers
    {
        private static int _warned;

        private static void Postfix(ZNet __instance, List<ZNet.PlayerInfo> playerList)
        {
            try
            {
                if (playerList == null || __instance == null) return;
                if (!RavenEyeTick.EvaluateGrant(__instance)) { RosterSync.LastAppended = 0; return; }

                RosterSync.LastAppended = RosterMerge.Append(playerList, RosterSync.Current(__instance), __instance.LocalPlayerCharacterID);
            }
            catch (Exception ex)
            {
                if (_warned++ < 3)
                    RavenEye.Log.LogWarning($"GetOtherPublicPlayers postfix threw ({ex.GetType().Name}: {ex.Message}); vanilla's list stands.");
            }
        }
    }
}
