namespace Nom.Api.Controllers
{
    public class ClonePlanRequest
    {
        public long SourcePlanId { get; set; }
        public string NewPlanName { get; set; } = string.Empty;
    }
}
