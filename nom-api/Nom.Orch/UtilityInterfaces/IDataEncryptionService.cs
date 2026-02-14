namespace Nom.Orch.UtilityInterfaces
{
    /// <summary>
    /// Interface for Data Encryption service
    /// </summary>
    public interface IDataEncryptionService
    {
        /// <summary>
        /// Encrypts a string using AES-256
        /// </summary>
        /// <param name="plainText">The text to encrypt</param>
        /// <returns>The encrypted string</returns>
        string EncryptString(string plainText);

        /// <summary>
        /// Decrypts a string using AES-256
        /// </summary>
        /// <param name="cipherText">The encrypted text</param>
        /// <returns>The decrypted string</returns>
        string DecryptString(string cipherText);

        /// <summary>
        /// Encrypts a byte array using AES-256
        /// </summary>
        /// <param name="plainBytes">The bytes to encrypt</param>
        /// <returns>The encrypted bytes</returns>
        byte[] EncryptBytes(byte[] plainBytes);

        /// <summary>
        /// Decrypts a byte array using AES-256
        /// </summary>
        /// <param name="cipherBytes">The encrypted bytes</param>
        /// <returns>The decrypted bytes</returns>
        byte[] DecryptBytes(byte[] cipherBytes);

        /// <summary>
        /// Encrypts a file asynchronously
        /// </summary>
        /// <param name="fileBytes">The file bytes to encrypt</param>
        /// <returns>The encrypted file bytes</returns>
        Task<byte[]> EncryptFileAsync(byte[] fileBytes);

        /// <summary>
        /// Decrypts a file asynchronously
        /// </summary>
        /// <param name="encryptedBytes">The encrypted file bytes</param>
        /// <returns>The decrypted file bytes</returns>
        Task<byte[]> DecryptFileAsync(byte[] encryptedBytes);

        /// <summary>
        /// Generates a new encryption key
        /// </summary>
        /// <returns>The generated key</returns>
        static abstract string GenerateEncryptionKey();

        /// <summary>
        /// Generates a new initialization vector
        /// </summary>
        /// <returns>The generated IV</returns>
        static abstract string GenerateInitializationVector();

        /// <summary>
        /// Checks if a value is encrypted
        /// </summary>
        /// <param name="value">The value to check</param>
        /// <returns>True if encrypted</returns>
        bool IsEncrypted(string value);
    }
} 