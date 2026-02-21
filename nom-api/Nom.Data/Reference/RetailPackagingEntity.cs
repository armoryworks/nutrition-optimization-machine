using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nom.Data.Reference;

/// <summary>
/// Stores how ingredients are commonly sold in retail stores.
/// Used to convert recipe quantities into practical shopping units (e.g., "2 cans" instead of "27 fl oz").
/// Data can come from seed data, manual entry, or future automated enrichment (web search, AI).
/// </summary>
[Table("RetailPackaging", Schema = "reference")]
public class RetailPackagingEntity : BaseEntity
{
    /// <summary>
    /// Ingredient name pattern to match (case-insensitive).
    /// Can be exact ("coconut milk") or partial ("cheese").
    /// More specific (longer) patterns take priority.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string IngredientPattern { get; set; } = string.Empty;

    /// <summary>
    /// Package type name: "can", "box", "bag", "jar", "bottle", "carton",
    /// "container", "bunch", "head", "loaf", "stick", "block", "wedge", "pint", "pack", "dozen"
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string PackageName { get; set; } = string.Empty;

    /// <summary>
    /// Display size of one package (e.g., 13.5 for a 13.5 fl oz can)
    /// </summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal PackageSize { get; set; }

    /// <summary>
    /// Display unit for the package size: "fl oz", "oz", "lb", "ct"
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string PackageSizeUnit { get; set; } = string.Empty;

    /// <summary>
    /// Measurement category: "volume", "mass", or "count"
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string SizeCategory { get; set; } = string.Empty;

    /// <summary>
    /// Package size in base units (ml for volume, g for mass, count for count).
    /// Used for calculating how many packages are needed.
    /// </summary>
    [Column(TypeName = "decimal(12,4)")]
    public decimal SizeInBaseUnits { get; set; }

    /// <summary>
    /// Whether this is the default/most common retail size for this ingredient.
    /// </summary>
    public bool IsDefault { get; set; } = true;

    /// <summary>
    /// Data provenance: "seed", "manual", "web-search", "ai"
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Source { get; set; } = "seed";
}
