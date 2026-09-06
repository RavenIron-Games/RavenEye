using System.Collections.Generic;
using RavenIron.RavenEye.Net;

namespace RavenIron.RavenEye.Core
{
    /// <summary>
    /// Where each connected player last stood alive, and where each one died — as a pure
    /// ledger over opaque keys (peer uids on a server; the host under its own key), so the
    /// off-game harness can drive it through death, respawn, expiry and disconnect.
    ///
    /// The first draft kept the death snapshot in the SAME slot as the live entry and served
    /// it only while the character id was None. Vanilla clears the id about ten seconds after
    /// death and restores it at respawn, so the "(dead)" pin lived for one or two cadences
    /// and vanished the moment the player respawned — the exact moment they ask "where did I
    /// die?". The code review caught it. Two stores now: the live ledger and the deaths, and
    /// a death is served until its grace elapses or the player disconnects, ALONGSIDE the
    /// respawned player's live pin. They carry different character ids (a respawn is a new
    /// ZDO), so vanilla's pin code keeps both.
    ///
    /// A death is detected two ways: the id going None (the normal path), or the id CHANGING
    /// between two cadences with no None observed (a cadence that straddled the whole
    /// death-to-respawn window). Either way the snapshot is the last LIVE entry — never the
    /// reference position while dead, which vanilla reassigns.
    /// </summary>
    public sealed class DeathLedger
    {
        private struct Death
        {
            public RosterEntry Entry;
            public float DiedAt;
        }

        private readonly Dictionary<long, RosterEntry> _live = new Dictionary<long, RosterEntry>(16);
        private readonly Dictionary<long, Death> _dead = new Dictionary<long, Death>(16);
        private readonly List<long> _scratch = new List<long>(16);

        public int LiveCount => _live.Count;
        public int DeadCount => _dead.Count;

        /// <summary>
        /// One connected player, this cadence. `hasCharacter` false means between death and
        /// respawn, or not yet spawned; `live` is ignored then.
        /// </summary>
        public void Observe(long key, bool hasCharacter, RosterEntry live, float now)
        {
            bool hadLive = _live.TryGetValue(key, out RosterEntry prev);

            if (hasCharacter)
            {
                if (hadLive && prev.Id != live.Id)
                    _dead[key] = new Death { Entry = prev, DiedAt = now };   // respawned between cadences
                live.Dead = false;
                _live[key] = live;
                return;
            }

            if (hadLive)
            {
                _dead[key] = new Death { Entry = prev, DiedAt = now };
                _live.Remove(key);
            }
        }

        /// <summary>
        /// Append every unexpired death, marked dead, and forget the expired ones. A grace of
        /// zero or less disables the feature and clears the store.
        /// </summary>
        /// <returns>How many were appended.</returns>
        public int Emit(List<RosterEntry> into, float now, float graceSeconds)
        {
            if (graceSeconds <= 0f)
            {
                _dead.Clear();
                return 0;
            }

            int appended = 0;
            _scratch.Clear();
            foreach (KeyValuePair<long, Death> kv in _dead)
            {
                if (now - kv.Value.DiedAt > graceSeconds)
                {
                    _scratch.Add(kv.Key);
                    continue;
                }
                RosterEntry e = kv.Value.Entry;
                e.Dead = true;
                if (into != null) into.Add(e);
                appended++;
            }
            for (int i = 0; i < _scratch.Count; i++) _dead.Remove(_scratch[i]);
            return appended;
        }

        /// <summary>Forget everyone whose key is not in <paramref name="present"/> — they disconnected.</summary>
        public void Retain(List<long> present)
        {
            Prune(_live, present);
            Prune(_dead, present);
        }

        public void Clear()
        {
            _live.Clear();
            _dead.Clear();
        }

        private void Prune<T>(Dictionary<long, T> store, List<long> present)
        {
            if (store.Count == 0) return;
            _scratch.Clear();
            foreach (long key in store.Keys)
            {
                if (present == null || !present.Contains(key)) _scratch.Add(key);
            }
            for (int i = 0; i < _scratch.Count; i++) store.Remove(_scratch[i]);
        }
    }
}
