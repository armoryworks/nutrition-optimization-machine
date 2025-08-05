using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Nom.Orch.UtilityInterfaces;
using System.Collections.Generic; // Added for List

namespace Nom.Orch.UtilityServices
{
    /// <summary>
    /// Multi-Factor Authentication service using TOTP (Time-based One-Time Password)
    /// Provides secure MFA implementation compatible with authenticator apps
    /// </summary>
    public class MultiFactorAuthenticationService : IMultiFactorAuthenticationService
    {
        private readonly ILogger<MultiFactorAuthenticationService> _logger;
        private readonly IConfiguration _configuration;

        // TOTP configuration
        private const int TOTP_DIGITS = 6;
        private const int TOTP_PERIOD = 30; // 30 seconds
        private const int TOTP_WINDOW = 1; // Allow 1 period before/after for clock skew

        public MultiFactorAuthenticationService(IConfiguration configuration, ILogger<MultiFactorAuthenticationService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// Generates a new MFA secret for a user
        /// </summary>
        public string GenerateMfaSecret(string userId)
        {
            try
            {
                // Generate a random 20-byte secret (160 bits)
                var secretBytes = new byte[20];
                RandomNumberGenerator.Fill(secretBytes);

                // Convert to base32 for compatibility with authenticator apps
                var secret = ConvertToBase32(secretBytes);

                _logger.LogInformation("Generated MFA secret for user {UserId}", userId);
                return secret;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate MFA secret for user {UserId}", userId);
                throw;
            }
        }

        /// <summary>
        /// Generates a QR code URL for authenticator app setup
        /// </summary>
        public string GenerateQrCodeUrl(string userId, string userEmail, string secret, string issuer = "NOM")
        {
            try
            {
                var encodedIssuer = Uri.EscapeDataString(issuer);
                var encodedAccount = Uri.EscapeDataString(userEmail);
                var encodedSecret = Uri.EscapeDataString(secret);

                var url = $"otpauth://totp/{encodedIssuer}:{encodedAccount}?secret={encodedSecret}&issuer={encodedIssuer}&algorithm=SHA1&digits={TOTP_DIGITS}&period={TOTP_PERIOD}";

                _logger.LogInformation("Generated QR code URL for user {UserId}", userId);
                return url;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate QR code URL for user {UserId}", userId);
                throw;
            }
        }

        /// <summary>
        /// Validates a TOTP code for a user
        /// </summary>
        public bool ValidateTotpCode(string secret, string code)
        {
            try
            {
                if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(code))
                {
                    return false;
                }

                // Convert base32 secret back to bytes
                var secretBytes = ConvertFromBase32(secret);
                if (secretBytes == null)
                {
                    return false;
                }

                // Get current timestamp
                var timestamp = GetCurrentTimestamp();

                // Check current and adjacent periods for clock skew
                for (int i = -TOTP_WINDOW; i <= TOTP_WINDOW; i++)
                {
                    var checkTimestamp = timestamp + (i * TOTP_PERIOD);
                    var expectedCode = GenerateTotpCode(secretBytes, checkTimestamp);

                    if (code == expectedCode)
                    {
                        _logger.LogInformation("Valid TOTP code provided");
                        return true;
                    }
                }

                _logger.LogWarning("Invalid TOTP code provided");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating TOTP code");
                return false;
            }
        }

        /// <summary>
        /// Generates a TOTP code for the current time
        /// </summary>
        public string GenerateCurrentTotpCode(string secret)
        {
            try
            {
                var secretBytes = ConvertFromBase32(secret);
                if (secretBytes == null)
                {
                    throw new ArgumentException("Invalid secret format");
                }

                var timestamp = GetCurrentTimestamp();
                return GenerateTotpCode(secretBytes, timestamp);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating current TOTP code");
                throw;
            }
        }

        /// <summary>
        /// Checks if MFA is required for a user
        /// </summary>
        public bool IsMfaRequired(string userId)
        {
            // In a real implementation, this would check user settings
            // For now, we'll require MFA for all users
            return true;
        }

        /// <summary>
        /// Generates a TOTP code for a specific timestamp
        /// </summary>
        private string GenerateTotpCode(byte[] secret, long timestamp)
        {
            // Convert timestamp to bytes (big-endian)
            var timeBytes = BitConverter.GetBytes(timestamp);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(timeBytes);
            }

            // Generate HMAC-SHA1
            using var hmac = new HMACSHA1(secret);
            var hash = hmac.ComputeHash(timeBytes);

            // Get offset from last 4 bits of hash
            var offset = hash[hash.Length - 1] & 0x0F;

            // Generate 4-byte code from hash
            var code = ((hash[offset] & 0x7F) << 24) |
                      ((hash[offset + 1] & 0xFF) << 16) |
                      ((hash[offset + 2] & 0xFF) << 8) |
                      (hash[offset + 3] & 0xFF);

            // Convert to specified number of digits
            var modulo = (int)Math.Pow(10, TOTP_DIGITS);
            var totpCode = code % modulo;

            return totpCode.ToString().PadLeft(TOTP_DIGITS, '0');
        }

        /// <summary>
        /// Gets the current Unix timestamp
        /// </summary>
        private long GetCurrentTimestamp()
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (long)(DateTime.UtcNow - epoch).TotalSeconds;
        }

        /// <summary>
        /// Converts bytes to base32 string
        /// </summary>
        private string ConvertToBase32(byte[] data)
        {
            const string base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            var result = new StringBuilder();

            int bitsLeft = data.Length * 8;
            int currentByte = 0;
            int nextByte = 1;
            int bitsRemaining = 8;

            while (bitsLeft > 0)
            {
                if (bitsRemaining < 5)
                {
                    if (nextByte < data.Length)
                    {
                        currentByte <<= 8;
                        currentByte |= data[nextByte] & 0xFF;
                        bitsRemaining += 8;
                        nextByte++;
                    }
                    else
                    {
                        int pad = 5 - bitsRemaining;
                        currentByte <<= pad;
                        bitsRemaining += pad;
                    }
                }

                bitsRemaining -= 5;
                result.Append(base32Chars[(currentByte >> bitsRemaining) & 0x1F]);
            }

            return result.ToString();
        }

        /// <summary>
        /// Converts base32 string to bytes
        /// </summary>
        private byte[]? ConvertFromBase32(string base32)
        {
            const string base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            base32 = base32.ToUpper().Replace(" ", "").Replace("-", "");

            if (base32.Length == 0)
                return null;

            var result = new List<byte>();
            int bitsLeft = base32.Length * 5;
            int currentByte = 0;
            int bitsRemaining = 8;

            foreach (char c in base32)
            {
                int value = base32Chars.IndexOf(c);
                if (value == -1)
                    return null;

                currentByte <<= 5;
                currentByte |= value & 0x1F;
                bitsRemaining -= 5;

                if (bitsRemaining <= 0)
                {
                    result.Add((byte)(currentByte >> -bitsRemaining));
                    currentByte &= (1 << -bitsRemaining) - 1;
                    bitsRemaining += 8;
                }
            }

            if (bitsRemaining < 8)
            {
                result.Add((byte)(currentByte << bitsRemaining));
            }

            return result.ToArray();
        }
    }
} 