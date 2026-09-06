namespace RavenIron.RavenEye.Core
{
    /// <summary>
    /// Is this client allowed the map right now? Pure arithmetic over explicit inputs so the
    /// off-game harness can test every edge, because this is the one decision in the mod
    /// that fails SILENTLY in both directions: too eager and a non-admin sees a map, too
    /// strict and an admin sees nothing and files a bug.
    ///
    /// Two mechanisms, deliberately. A DEMOTION is explicit: the server sends one revoke
    /// packet and the client drops the grant on receipt, within one interval. SILENCE is the
    /// backstop: if the server crashes, stalls or vanishes, the grant survives a generous,
    /// server-chosen grace window and then lapses. The two error costs are wildly unequal —
    /// a demoted admin keeping a map for another minute costs nothing; a false revoke during
    /// an autosave stall slams the map shut under the admin's cursor — so silence is slow
    /// and the explicit path is fast.
    /// </summary>
    public static class Grant
    {
        /// <summary>The received grace is clamped here: a malformed value cannot revoke early…</summary>
        public const float MinGraceSeconds = 10f;

        /// <summary>…nor keep a dead grant alive indefinitely.</summary>
        public const float MaxGraceSeconds = 120f;

        /// <summary>Sentinel for "no roster has arrived", and what a revoke resets the receipt to.</summary>
        public const float Never = -1f;

        /// <summary>The grace window the client actually honours for a server-sent value.</summary>
        public static float EffectiveGrace(float graceSeconds)
        {
            // Garbage (NaN, zero, negative, -inf) falls to the SHORTEST window, never the
            // longest: a malformed packet must not extend a grant.
            if (float.IsNaN(graceSeconds) || graceSeconds <= 0f) return MinGraceSeconds;
            if (graceSeconds < MinGraceSeconds) return MinGraceSeconds;
            if (graceSeconds > MaxGraceSeconds) return MaxGraceSeconds;
            return graceSeconds;
        }

        /// <summary>Has a granting roster arrived within the grace window?</summary>
        public static bool IsFresh(float now, float lastReceipt, float graceSeconds)
        {
            if (lastReceipt < 0f) return false;            // Never
            float age = now - lastReceipt;
            if (age < 0f) return false;                    // a clock that ran backwards is not evidence
            return age <= EffectiveGrace(graceSeconds);
        }

        /// <summary>Why the grant is what it is — for the status line, in words, not a boolean.</summary>
        public enum Reason
        {
            Granted,
            GrantedAsAuthority,
            ClientOff,
            NoRenderer,
            NotConnected,
            Revoked,          // the server said so, explicitly
            NeverReceived,    // nothing has arrived since this join
            Stale,            // the server went silent past the grace window
        }

        /// <summary>
        /// The whole decision, with its reason. `isAuthority` is a server (either kind) with
        /// the feature on and the world calling for a roster: it has no round-trip to itself
        /// to wait on, and vanilla already treats the host as admin. `connected` is "the
        /// server peer still exists" — a dropped connection revokes at once. `lastRevoke` is
        /// when the server last said "not granted"; a revoke also resets `lastReceipt` to
        /// Never, so it is here only to tell the reason apart from "never received".
        /// </summary>
        public static Reason Explain(bool showMap, bool hasRenderer, bool isAuthority, bool connected,
                                     float now, float lastReceipt, float lastRevoke, float graceSeconds)
        {
            if (!showMap) return Reason.ClientOff;
            if (!hasRenderer) return Reason.NoRenderer;
            if (isAuthority) return Reason.GrantedAsAuthority;
            if (!connected) return Reason.NotConnected;
            if (IsFresh(now, lastReceipt, graceSeconds)) return Reason.Granted;
            if (lastReceipt < 0f) return lastRevoke >= 0f ? Reason.Revoked : Reason.NeverReceived;
            return Reason.Stale;
        }

        public static bool IsGranted(Reason r) => r == Reason.Granted || r == Reason.GrantedAsAuthority;

        /// <summary>The boolean form of <see cref="Explain"/>.</summary>
        public static bool Evaluate(bool showMap, bool hasRenderer, bool isAuthority, bool connected,
                                    float now, float lastReceipt, float graceSeconds)
        {
            return IsGranted(Explain(showMap, hasRenderer, isAuthority, connected, now, lastReceipt, Never, graceSeconds));
        }

        /// <summary>
        /// What `Game.m_noMap` SHOULD be. Vanilla's own arithmetic is `world || personal`;
        /// a grant relaxes exactly the world half and keeps the character's own opt-out.
        /// The client converges the live flag to this every half second, so a revoke that
        /// lands while the player is dead — when the flag cannot be re-applied — is applied
        /// the moment it can be, rather than dropped (the critique's blocker).
        /// </summary>
        public static bool DesiredNoMap(bool granted, bool worldNoMap, bool personalOptOut)
        {
            return granted ? personalOptOut : (worldNoMap || personalOptOut);
        }
    }

    /// <summary>
    /// Edge detector for the grant — for LOGGING the change, not for applying it. Applying
    /// is convergent (see <see cref="Grant.DesiredNoMap"/>), because an edge that fires
    /// while it cannot be acted on is an edge lost.
    /// </summary>
    public sealed class GrantTracker
    {
        public bool Granted { get; private set; }

        /// <summary>Record the current value; true when it differs from the last one.</summary>
        public bool Update(bool granted)
        {
            if (granted == Granted) return false;
            Granted = granted;
            return true;
        }

        public void Reset() => Granted = false;
    }
}
