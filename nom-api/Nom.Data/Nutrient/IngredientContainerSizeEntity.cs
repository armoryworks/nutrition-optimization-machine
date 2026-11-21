using System;

namespace Nom.Data.Nutrient
{
    /// <summary>
    /// The size of one container of a food ("1 can" of canned black beans = 425 g), per food,
    /// container kind, era and size word, with the deterministic source it came from. Recipe lines
    /// are never rewritten from it: an unsized can, jar, bottle, box or package line is weighed at
    /// the size of its food's active row (the container-weighing item reads it). Every write,
    /// status change and undo is journaled in audit.AuditLogEntry. See
    /// docs/architecture/food-catalog-ingestion.md, "Container sizes".
    /// </summary>
    public class IngredientContainerSizeEntity : BaseEntity
    {
        public long IngredientId { get; set; }

        /// <summary>One of <c>ContainerKinds</c> (can, jar, bottle, box, bag, packet, envelope, carton, tub, package).</summary>
        public string ContainerKind { get; set; } = string.Empty;

        /// <summary>modern, historic or any.</summary>
        public string Era { get; set; } = string.Empty;

        /// <summary>Empty for the usual size; small or large for the size a line names with that word.</summary>
        public string SizeWord { get; set; } = string.Empty;

        /// <summary>The net weight of one container, liquid included.</summary>
        public decimal NetGrams { get; set; }

        /// <summary>The drained weight of one container; null when the food is not packed in liquid or no drained weight is known.</summary>
        public decimal? DrainedGrams { get; set; }

        /// <summary>How the size reads to a cook ("15 oz (425 g) can").</summary>
        public string SizeText { get; set; } = string.Empty;

        /// <summary>The source, always deterministic: fdc:{id}:portion:{seq}, deterministic:fdc-branded-mode:..., deterministic:can-table:{row} or deterministic:standard-of-fill:{section}.</summary>
        public string SourceRef { get; set; } = string.Empty;

        /// <summary>The class the size is scored under (a branded class, or portion:{kind}, table:{kind}, fill:{kind}).</summary>
        public string Class { get; set; } = string.Empty;

        /// <summary>How many independent sources gave a size within 10 % of this one.</summary>
        public int SourcesAgreeing { get; set; }

        /// <summary>For a branded mode, the share of products in the mode's ±5 % band.</summary>
        public decimal? ModeShare { get; set; }

        /// <summary>For a branded mode, the products with a parsed size.</summary>
        public int? Products { get; set; }

        public int? HoldoutAgree { get; set; }

        public int? HoldoutLines { get; set; }

        /// <summary>active, shadow, paused or retired.</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Why the row has its status, in figures.</summary>
        public string? StatusReason { get; set; }

        public DateTime? ScoredAt { get; set; }
    }
}
