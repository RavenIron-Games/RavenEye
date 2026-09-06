using System.Collections.Generic;
using RavenIron.RavenEye.Net;

namespace RavenIron.RavenEye.Core
{
    /// <summary>
    /// The decoration applied to vanilla's public-player list, as a pure function.
    ///
    /// `Minimap.UpdatePlayerPins` asks `ZNet.GetOtherPublicPlayers` for the players to pin
    /// and draws whatever comes back. Vanilla fills that list with the players who share
    /// their position; this appends everyone else from the server's roster, in the server's
    /// order, so the same vanilla code draws the same vanilla pins for all of them.
    ///
    /// Two exclusions: the local character (vanilla excludes it too — you are the marker,
    /// not a pin), and anyone already in the list by character id (a player who shares
    /// position is in vanilla's copy already; a second pin would flicker between the two
    /// sources' slightly different timestamps).
    ///
    /// A dead entry keeps its last known position and gets "(dead)" after the name — the
    /// name is the only field vanilla's pin shows, so it is the only place to say so.
    ///
    /// Runs once per rendered frame over a handful of entries with no allocation beyond
    /// the list's own growth; the data it merges changes at most once per interval.
    /// </summary>
    public static class RosterMerge
    {
        public const string DeadSuffix = " (dead)";

        /// <returns>How many entries were appended.</returns>
        public static int Append(List<ZNet.PlayerInfo> target, IList<RosterEntry> roster, ZDOID self)
        {
            if (target == null || roster == null) return 0;

            int added = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                RosterEntry e = roster[i];
                if (e.Id.IsNone()) continue;           // nothing to point at
                if (e.Id == self) continue;
                if (ContainsCharacter(target, e.Id)) continue;

                target.Add(new ZNet.PlayerInfo
                {
                    m_name = (e.Name ?? "") + (e.Dead ? DeadSuffix : ""),
                    m_characterID = e.Id,
                    m_position = e.Position,
                    m_publicPosition = true,
                });
                added++;
            }
            return added;
        }

        private static bool ContainsCharacter(List<ZNet.PlayerInfo> list, ZDOID id)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].m_characterID == id) return true;
            }
            return false;
        }
    }
}
