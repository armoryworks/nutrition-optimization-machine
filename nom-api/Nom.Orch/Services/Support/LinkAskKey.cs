using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The question a link pick answered: the picker model, its prompt version, the normalised
    /// wording and the shortlist it was shown. It is written at the end of a model proposal's
    /// reason as " · ask: {model} prompt {version} #{hash}", so a machine rejection is asked again
    /// only when the question changed, and picks can be measured by prompt.
    /// </summary>
    public static class LinkAskKey
    {
        public const string Mark = " · ask: ";

        /// <summary>The part of the mark that names the model and prompt, which a query can match without the shortlist.</summary>
        public static string Tag(string model, int promptVersion) => $"{Mark}{model} prompt {promptVersion} #";

        public static string For(string model, int promptVersion, string normalizedName, IEnumerable<FoodCandidate> shortlist)
        {
            var question = string.Join("\n", new[] { model, promptVersion.ToString(), normalizedName }.Concat(shortlist.Select(c => c.FdcId)));
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(question)))[..12].ToLowerInvariant();
            return Tag(model, promptVersion) + hash;
        }
    }
}
