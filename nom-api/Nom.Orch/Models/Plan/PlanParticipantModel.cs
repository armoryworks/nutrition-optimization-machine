using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Plan
{
    public class PlanParticipantModel
    {
        public long Id { get; set; }
        public long PersonId { get; set; }
        public string PersonName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public DateTime JoinedDate { get; set; }
    }
} 