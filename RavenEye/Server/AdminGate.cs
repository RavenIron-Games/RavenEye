using System;
using System.Runtime.CompilerServices;

namespace RavenIron.RavenEye.Server
{
    /// <summary>
    /// "Is this peer an admin?" — answered by vanilla's own PUBLIC `ZNet.IsAdmin(string)`,
    /// whose entire body is `ListContainsId(m_adminList, hostName)`: the same check the game
    /// applies before it honours a remote console command, a kick or a ban. It parses the
    /// host name as a platform id and accepts both the prefixed and the bare form, which is
    /// why a bare Steam64 in adminlist.txt works. (`ZNet.PlayerIsAdmin(PlatformUserID)`
    /// checks only one form and is NOT equivalent. Decompile-verified 2026-09-06, 0.221.12,
    /// real assembly: `public bool IsAdmin(string hostName)` at ZNet.cs:2592.)
    ///
    /// The first draft reflected into the private `ListContainsId` with cached delegates,
    /// fail-closed, retried, error-logged — the whole rule-5 apparatus — because a grep of
    /// the public surface was cut short and missed the wrapper. The design critique found
    /// it. Nothing here is private now, so rule 5 does not apply; this class remains only so
    /// that exactly ONE method in the mod names the game's admin API.
    ///
    /// FAIL CLOSED regardless: any exception is "not an admin", logged once.
    /// </summary>
    public static class AdminGate
    {
        private static bool _failureLogged;

        /// <summary>The decision. Any doubt is a no.</summary>
        public static bool IsAdmin(ZNet znet, ZNetPeer peer)
        {
            if (znet == null || peer == null) return false;

            string host;
            try { host = peer.m_socket?.GetHostName(); }
            catch { return false; }
            if (string.IsNullOrEmpty(host)) return false;

            try
            {
                return Check(znet, host);
            }
            catch (Exception ex)
            {
                if (!_failureLogged)
                {
                    _failureLogged = true;
                    RavenEye.Log.LogError(
                        $"AdminGate: ZNet.IsAdmin(string) threw {ex.GetType().Name}: {ex.Message}. " +
                        "Nobody will be granted the map. If this is a MissingMethodException, Valheim's API moved.");
                }
                return false;
            }
        }

        /// <summary>
        /// Isolated and never inlined so that, should `ZNet.IsAdmin(string)` ever disappear,
        /// the MissingMethodException is raised at THIS call — inside the try above — rather
        /// than while compiling a method that also does the sending.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool Check(ZNet znet, string host) => znet.IsAdmin(host);

        /// <summary>
        /// How many entries the server's admin list holds right now, or -1 if unreadable.
        /// On the server, `GetAdminList()` is the live list behind the file — the same
        /// instance `SyncedList` clears and refills on reload — so a 0 here while players
        /// are online is the tell for vanilla's silent-failure mode: `SyncedList.Load`
        /// clears the list BEFORE reading, latches the file's mtime, and swallows any
        /// exception, so a file that was open in an editor at reload time reads as empty
        /// until its timestamp changes again.
        /// </summary>
        public static int AdminListCount(ZNet znet)
        {
            try { return znet?.GetAdminList()?.Count ?? -1; }
            catch { return -1; }
        }
    }
}
