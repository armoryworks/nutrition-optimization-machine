using System;

namespace Nom.Orch.Models.Platform
{
    public class PlatformFeatureModel
    {
        public string Key { get; set; } = string.Empty;
        public string? Label { get; set; }
        public bool IsEnabled { get; set; }
        public string? Description { get; set; }
        public DateTime? CreatedDate { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime? LastModifiedDate { get; set; }
        public string? LastModifiedByName { get; set; }
    }

    public class SetPlatformFeatureRequestModel
    {
        public bool IsEnabled { get; set; }
    }
}
