using System;

namespace Nom.Orch.Models.Platform
{
    public class PlatformFeatureModel
    {
        public string Key { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public string? Description { get; set; }
        public DateTime? LastModifiedDate { get; set; }
    }

    public class SetPlatformFeatureRequestModel
    {
        public bool IsEnabled { get; set; }
    }
}
