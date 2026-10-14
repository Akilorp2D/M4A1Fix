using System;
using System.Reflection;

namespace M4A1Fix.Compatibility
{
    internal static class MethodLookup
    {
        private const BindingFlags DeclaredMethodFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        internal static bool TryResolveSingleDeclaredMethod(Type type, string methodName, out MethodInfo method, out string reason)
        {
            method = null;
            reason = null;
            int count = 0;

            foreach (MethodInfo candidate in type.GetMethods(DeclaredMethodFlags))
            {
                if (candidate.Name != methodName)
                    continue;

                method = candidate;
                count++;
            }

            if (count == 1)
                return true;

            method = null;
            reason = count == 0
                ? "exact method is missing: " + type.FullName + "." + methodName
                : "exact method resolution is ambiguous: " + type.FullName + "." + methodName + " (count=" + count + ")";
            return false;
        }
    }
}
