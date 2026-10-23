using System.Linq;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// GTIN-8/12/13/14 normalisation: digits only, a length GS1 defines, and a valid mod-10 check
    /// digit. Anything else is refused rather than stored, because a barcode is a join key.
    /// </summary>
    public static class Gtin
    {
        public static bool TryNormalize(string? raw, out string digits)
        {
            digits = new string((raw ?? string.Empty).Where(char.IsDigit).ToArray());
            if (digits.Length is not (8 or 12 or 13 or 14))
                return false;

            var sum = 0;
            var weight = 3;
            for (var i = digits.Length - 2; i >= 0; i--)
            {
                sum += (digits[i] - '0') * weight;
                weight = weight == 3 ? 1 : 3;
            }
            var check = (10 - sum % 10) % 10;
            return check == digits[^1] - '0';
        }
    }
}
