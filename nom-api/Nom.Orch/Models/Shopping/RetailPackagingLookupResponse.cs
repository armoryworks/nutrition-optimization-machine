using System.Collections.Generic;

namespace Nom.Orch.Models.Shopping
{
    public class RetailPackagingLookupResponse
    {
        public List<RetailPackagingResponseModel> Results { get; set; } = new();
        public List<string> NotFound { get; set; } = new();
        public bool AiLookupPerformed { get; set; }
    }
}
