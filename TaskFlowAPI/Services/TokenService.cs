using System;
using System.Security.Cryptography;
using System.Text;

namespace TaskFlowAPI.Services
{
    public class TokenService
    {
        // Generate a secure, unguessable plain text token for the user
        public (string PlainTextToken, string HashedToken) GenerateTokenPair()
        {
            var randomBytes = new byte[64];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }

            string plainTextToken = Convert.ToBase64String(randomBytes);
            string hashedToken = HashToken(plainTextToken);

            return (plainTextToken, hashedToken);
        }

        // Compute a SHA256 Hash of the token string
        public string HashToken(string plainTextToken)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(plainTextToken));
                return Convert.ToHexString(hashBytes); // Clean, easy-to-index string representation
            }
        }
    }
}
