using System;
using System.Collections.Generic;

namespace RavenIron.RavenEye.Core
{
    /// <summary>
    /// Installs the patch classes one at a time, so a single target that no longer resolves
    /// after a Valheim update costs that one feature and not the whole mod. `PatchAll` throws
    /// on the first "Undefined target method" and everything after it — including the
    /// `raveneye` console that would explain what broke — never happens.
    ///
    /// Pure over its inputs so the harness can prove that a throwing class is skipped, named
    /// and does not stop the others.
    /// </summary>
    public static class PatchInstall
    {
        /// <summary>
        /// Runs <paramref name="install"/> for every class, catching per class. Returns the
        /// names of the classes that failed (empty when all installed).
        /// </summary>
        public static List<string> Each(IEnumerable<Type> classes, Action<Type> install, Action<Type, Exception> onFailure)
        {
            var failed = new List<string>();
            foreach (Type t in classes)
            {
                try
                {
                    install(t);
                }
                catch (Exception ex)
                {
                    failed.Add(t.Name);
                    try { onFailure?.Invoke(t, ex); } catch { /* logging must not stop the loop */ }
                }
            }
            return failed;
        }
    }
}
