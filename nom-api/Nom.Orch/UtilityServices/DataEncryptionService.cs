using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Orch.UtilityServices
{
    /// <summary>
    /// Data encryption service for encrypting sensitive data at rest
    /// Provides AES encryption for database fields and file storage
    /// </summary>
    public class DataEncryptionService : IDataEncryptionService
    {
        private readonly ILogger<DataEncryptionService> _logger;
        private readonly byte[] _encryptionKey;
        private readonly byte[] _initializationVector;

        public DataEncryptionService(IConfiguration configuration, ILogger<DataEncryptionService> logger)
        {
            _logger = logger;

            // Get encryption key from configuration or generate a new one
            var keyString = configuration["Encryption:Key"];
            if (string.IsNullOrEmpty(keyString))
            {
                _logger.LogWarning("No encryption key found in configuration. Using default key (NOT SECURE FOR PRODUCTION)");
                keyString = "DefaultEncryptionKeyForDevelopmentOnly32Bytes!";
            }

            // Ensure key is exactly 32 bytes (256 bits) for AES-256
            var keyBytes = Encoding.UTF8.GetBytes(keyString);
            if (keyBytes.Length != 32)
            {
                Array.Resize(ref keyBytes, 32);
            }
            _encryptionKey = keyBytes;

            // Get IV from configuration or generate a new one
            var ivString = configuration["Encryption:IV"];
            if (string.IsNullOrEmpty(ivString))
            {
                _logger.LogWarning("No encryption IV found in configuration. Using default IV (NOT SECURE FOR PRODUCTION)");
                ivString = "DefaultIV16Bytes!!";
            }

            // Ensure IV is exactly 16 bytes for AES
            var ivBytes = Encoding.UTF8.GetBytes(ivString);
            if (ivBytes.Length != 16)
            {
                Array.Resize(ref ivBytes, 16);
            }
            _initializationVector = ivBytes;
        }

        /// <summary>
        /// Encrypts a string value
        /// </summary>
        public string EncryptString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return plainText;

            try
            {
                using var aes = Aes.Create();
                aes.Key = _encryptionKey;
                aes.IV = _initializationVector;

                using var encryptor = aes.CreateEncryptor();
                using var msEncrypt = new MemoryStream();
                using var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write);
                using var swEncrypt = new StreamWriter(csEncrypt);

                swEncrypt.Write(plainText);
                swEncrypt.Flush();
                csEncrypt.FlushFinalBlock();

                var encryptedBytes = msEncrypt.ToArray();
                return Convert.ToBase64String(encryptedBytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to encrypt string");
                throw new InvalidOperationException("Failed to encrypt data", ex);
            }
        }

        /// <summary>
        /// Decrypts an encrypted string value
        /// </summary>
        public string DecryptString(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText))
                return cipherText;

            try
            {
                var cipherBytes = Convert.FromBase64String(cipherText);

                using var aes = Aes.Create();
                aes.Key = _encryptionKey;
                aes.IV = _initializationVector;

                using var decryptor = aes.CreateDecryptor();
                using var msDecrypt = new MemoryStream(cipherBytes);
                using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
                using var srDecrypt = new StreamReader(csDecrypt);

                return srDecrypt.ReadToEnd();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt string");
                throw new InvalidOperationException("Failed to decrypt data", ex);
            }
        }

        /// <summary>
        /// Encrypts a byte array
        /// </summary>
        public byte[] EncryptBytes(byte[] plainBytes)
        {
            if (plainBytes == null || plainBytes.Length == 0)
                return plainBytes;

            try
            {
                using var aes = Aes.Create();
                aes.Key = _encryptionKey;
                aes.IV = _initializationVector;

                using var encryptor = aes.CreateEncryptor();
                using var msEncrypt = new MemoryStream();
                using var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write);

                csEncrypt.Write(plainBytes, 0, plainBytes.Length);
                csEncrypt.FlushFinalBlock();

                return msEncrypt.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to encrypt bytes");
                throw new InvalidOperationException("Failed to encrypt data", ex);
            }
        }

        /// <summary>
        /// Decrypts an encrypted byte array
        /// </summary>
        public byte[] DecryptBytes(byte[] cipherBytes)
        {
            if (cipherBytes == null || cipherBytes.Length == 0)
                return cipherBytes;

            try
            {
                using var aes = Aes.Create();
                aes.Key = _encryptionKey;
                aes.IV = _initializationVector;

                using var decryptor = aes.CreateDecryptor();
                using var msDecrypt = new MemoryStream(cipherBytes);
                using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
                using var msResult = new MemoryStream();

                csDecrypt.CopyTo(msResult);
                return msResult.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt bytes");
                throw new InvalidOperationException("Failed to decrypt data", ex);
            }
        }

        /// <summary>
        /// Encrypts a file
        /// </summary>
        public async Task<byte[]> EncryptFileAsync(byte[] fileBytes)
        {
            if (fileBytes == null || fileBytes.Length == 0)
                return fileBytes;

            try
            {
                using var aes = Aes.Create();
                aes.Key = _encryptionKey;
                aes.IV = _initializationVector;

                using var encryptor = aes.CreateEncryptor();
                using var msEncrypt = new MemoryStream();
                using var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write);

                await csEncrypt.WriteAsync(fileBytes, 0, fileBytes.Length);
                await csEncrypt.FlushAsync();
                csEncrypt.FlushFinalBlock();

                return msEncrypt.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to encrypt file");
                throw new InvalidOperationException("Failed to encrypt file", ex);
            }
        }

        /// <summary>
        /// Decrypts an encrypted file
        /// </summary>
        public async Task<byte[]> DecryptFileAsync(byte[] encryptedBytes)
        {
            if (encryptedBytes == null || encryptedBytes.Length == 0)
                return encryptedBytes;

            try
            {
                using var aes = Aes.Create();
                aes.Key = _encryptionKey;
                aes.IV = _initializationVector;

                using var decryptor = aes.CreateDecryptor();
                using var msDecrypt = new MemoryStream(encryptedBytes);
                using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
                using var msResult = new MemoryStream();

                await csDecrypt.CopyToAsync(msResult);
                return msResult.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt file");
                throw new InvalidOperationException("Failed to decrypt file", ex);
            }
        }

        /// <summary>
        /// Generates a secure encryption key
        /// </summary>
        public static string GenerateEncryptionKey()
        {
            var keyBytes = new byte[32];
            RandomNumberGenerator.Fill(keyBytes);
            return Convert.ToBase64String(keyBytes);
        }

        /// <summary>
        /// Generates a secure initialization vector
        /// </summary>
        public static string GenerateInitializationVector()
        {
            var ivBytes = new byte[16];
            RandomNumberGenerator.Fill(ivBytes);
            return Convert.ToBase64String(ivBytes);
        }

        /// <summary>
        /// Checks if a string is encrypted
        /// </summary>
        public bool IsEncrypted(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            try
            {
                // Try to decode as base64 and check if it's valid encrypted data
                var bytes = Convert.FromBase64String(value);
                return bytes.Length > 16; // Encrypted data should be longer than IV
            }
            catch
            {
                return false;
            }
        }
    }
} 