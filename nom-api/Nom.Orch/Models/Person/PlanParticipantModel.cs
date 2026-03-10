namespace Nom.Orch.Models.Person
{
    public class PlanParticipantModel
    {
        public long Id { get; set; }
        public long PlanId { get; set; }
        public string PlanName { get; set; } = string.Empty;
        public long PersonId { get; set; }
        public string PersonName { get; set; } = string.Empty;
        public long RoleId { get; set; }
        public string RoleName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }
}
