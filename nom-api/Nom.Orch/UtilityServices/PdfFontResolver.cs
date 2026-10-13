using System;
using System.Collections.Concurrent;
using System.IO;
using PdfSharp.Fonts;

namespace Nom.Orch.UtilityServices
{
    /// <summary>
    /// Serves the Lato faces (SIL Open Font License 1.1) embedded in this assembly to PDFsharp, so PDF
    /// rendering never depends on fonts installed on the host — the aspnet container has none.
    /// Every family name resolves to Lato; <see cref="SemiboldFamily"/> selects the semibold face.
    /// </summary>
    public sealed class PdfFontResolver : IFontResolver
    {
        public const string Family = "Lato";
        public const string SemiboldFamily = "Lato Semibold";

        private static readonly object InstallLock = new();
        private static readonly ConcurrentDictionary<string, byte[]> Faces = new();

        public static void EnsureInstalled()
        {
            if (GlobalFontSettings.FontResolver is PdfFontResolver) return;
            lock (InstallLock)
            {
                if (GlobalFontSettings.FontResolver is not PdfFontResolver)
                    GlobalFontSettings.FontResolver = new PdfFontResolver();
            }
        }

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            if (string.Equals(familyName, SemiboldFamily, StringComparison.OrdinalIgnoreCase))
                return new FontResolverInfo("Lato-Semibold");
            if (isBold)
                return new FontResolverInfo("Lato-Bold", false, isItalic);
            return new FontResolverInfo(isItalic ? "Lato-Italic" : "Lato-Regular");
        }

        public byte[]? GetFont(string faceName) => Faces.GetOrAdd(faceName, Load);

        private static byte[] Load(string faceName)
        {
            var resource = $"Nom.Orch.Fonts.Lato.{faceName}.ttf";
            using var stream = typeof(PdfFontResolver).Assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Embedded font {resource} is missing.");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }
}
