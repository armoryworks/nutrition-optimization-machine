using System.ComponentModel.DataAnnotations.Schema;

namespace Nom.Data.Reference
{
    /// <summary>
    /// Abstract base class for grouped reference view entities, implementing TPH inheritance
    /// based on the GroupId.
    /// </summary>
    public abstract class GroupedReferenceViewEntity
    {
        // Properties to match the columns selected in the SQL View (reference.ReferenceGroupView)

        // From ReferenceEntity (these are properties from the base view class)
        public long ReferenceId { get; set; }
        public string ReferenceName { get; set; } = string.Empty;
        public string? ReferenceDescription { get; set; }

        // From GroupEntity (this property is now also the TPH discriminator for this hierarchy)
        public long GroupId { get; set; }
        public string GroupName { get; set; } = string.Empty;
        public string? GroupDescription { get; set; }
    }
}