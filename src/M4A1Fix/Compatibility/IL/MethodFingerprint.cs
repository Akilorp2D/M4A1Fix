using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace M4A1Fix.Compatibility.IL
{
    internal static class MethodFingerprint
    {
        internal static bool TryValidate(
            MethodInfo method,
            int expectedCodeSize,
            string expectedHash,
            string displayName,
            out List<DecodedInstruction> instructions,
            out string reason)
        {
            reason = null;
            if (!IlMethodReader.TryRead(method, expectedCodeSize, displayName, out instructions, out reason))
                return false;
            if (!CanonicalMethodFormatter.TryBuildCanonicalMethod(method, expectedCodeSize, displayName, instructions, out string canonical, out reason))
                return false;

            string actual = Sha256Hash.Compute(Encoding.UTF8.GetBytes(canonical));

            if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                reason = displayName + " canonical IL SHA-256 mismatch. Expected " + expectedHash + ", got " + actual + ".";
                return false;
            }

            return true;
        }

        internal static bool TryValidateRaw(MethodInfo method, string expected, string displayName, out string reason)
        {
            reason = null;
            MethodBody body = method.GetMethodBody();
            byte[] bytes = body != null ? body.GetILAsByteArray() : null;
            if (bytes == null)
            {
                reason = displayName + " has no inspectable IL body.";
                return false;
            }

            string actual = Sha256Hash.Compute(bytes);

            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                reason = displayName + " IL SHA-256 mismatch. Expected " + expected + ", got " + actual + ".";
                return false;
            }

            return true;
        }
    }
}
