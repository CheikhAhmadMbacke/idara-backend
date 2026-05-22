using System.Security.Cryptography;

namespace Idara.API.Common.Utilities
{
    public static class PasswordGenerator
    {
        private const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%";

        public static string Generate(int length = 12)
        {
            if (length < 4) length = 12;
            var buffer = new byte[length];
            RandomNumberGenerator.Fill(buffer);
            var sb = new char[length];
            for (var i = 0; i < length; i++)
            {
                sb[i] = Chars[buffer[i] % Chars.Length];
            }
            return new string(sb);
        }
    }
}
