using System;
using System.IO;
using System.Security.Cryptography;

namespace M4A1Fix.Compatibility
{
    internal static class Sha256Hash
    {
        internal static string Compute(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
                return Hex(sha.ComputeHash(bytes));
        }

        internal static string ComputeFile(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
                return Hex(sha.ComputeHash(stream));
        }

        private static string Hex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", string.Empty);
        }
    }
}
