using System.Threading.Tasks;

namespace Nom.Orch.UtilityInterfaces
{
    /// <summary>
    /// Optional filesystem-backed media storage. When configured (Media:RootPath
    /// pointing at a mounted volume — e.g. the fast local storage on the .56/.65
    /// servers), new media is written there and only a relative path is kept in
    /// the database. When not configured, media stays in the database as before.
    /// </summary>
    public interface IMediaStorageService
    {
        bool IsConfigured { get; }

        /// <summary>Writes the file and returns the relative path to store on the asset.</summary>
        Task<string> SaveAsync(string relativePath, byte[] data);

        /// <summary>Reads a previously saved file; null when missing.</summary>
        Task<byte[]?> ReadAsync(string relativePath);

        Task DeleteAsync(string relativePath);
    }
}
