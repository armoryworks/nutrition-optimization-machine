using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Person
{
    public class PersonModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? UserId { get; set; }
        public DateTime CreatedDate { get; set; }
        public long? CreatedByPersonId { get; set; }
        public List<PersonAttributeModel> Attributes { get; set; } = new List<PersonAttributeModel>();
        public List<PlanParticipantModel> PlanParticipations { get; set; } = new List<PlanParticipantModel>();
    }
}
