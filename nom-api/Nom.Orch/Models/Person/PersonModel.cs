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

    public class PersonAttributeModel
    {
        public long Id { get; set; }
        public long PersonId { get; set; }
        public long AttributeTypeId { get; set; }
        public string AttributeTypeName { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

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