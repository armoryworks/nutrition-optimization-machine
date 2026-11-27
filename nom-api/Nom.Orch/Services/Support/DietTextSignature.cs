using System;
using System.IO;
using System.Reflection;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The fingerprint of a derived table the diet gate reads from an embedded file (<see cref="BrandedConsensus"/>,
    /// <see cref="DietDishWords"/>): the FNV-1a hash of the file's bytes, so a file regenerated without its version
    /// raised is still another version of the store (<see cref="DietGateStore.Version"/>).
    /// </summary>
    public static class DietTextSignature
    {
        /// <summary>The hash of an embedded resource's bytes, as sixteen hexadecimal digits.</summary>
        public static string Of(Assembly assembly, string resource)
        {
            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"{resource} is not embedded in {assembly.GetName().Name}");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return Of(memory.ToArray());
        }

        /// <summary>The hash of a text's bytes, as sixteen hexadecimal digits.</summary>
        public static string Of(byte[] bytes)
        {
            var hash = 14695981039346656037UL;
            foreach (var b in bytes) hash = unchecked((hash ^ b) * 1099511628211UL);
            return hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
