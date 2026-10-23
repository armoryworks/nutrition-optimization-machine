namespace Nom.Orch.UtilityInterfaces
{
    public sealed record BarcodeDecodeResult(string Text, string Format);

    /// <summary>Finds a retail barcode (UPC-A/E, EAN-8/13) in a photo; null when there is none it can read.</summary>
    public interface IBarcodeDecodeService
    {
        BarcodeDecodeResult? Decode(byte[] image);
    }
}
