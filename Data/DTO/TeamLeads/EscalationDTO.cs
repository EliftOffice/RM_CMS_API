namespace RM_CMS.Data.DTO.TeamLeads
{
    public class EscalationDTO
    {
        public string EscalationId { get; set; }
        public string FollowUpId { get; set; }
        public string PersonId { get; set; }
        public string VolunteerId { get; set; }
        public string TeamLeadId { get; set; }

        // 🔥 CORE DETAILS
        public DateTime EscalationDate { get; set; }
        public string EscalationTier { get; set; }          // Standard / Urgent / Emergency
        public string EscalationReason { get; set; }        // ENUM from docs
        public string Description { get; set; }

        // 🔄 LIFECYCLE
        public string Status { get; set; }                  // New / In Progress / Resolved / Closed / Referred Out
        public string AssignedTo { get; set; }

        public DateTime? AcknowledgedAt { get; set; }
        public DateTime? ResolvedDate { get; set; }

        // 🧾 RESOLUTION
        public string? ResolutionNotes { get; set; }
        public string? Outcome { get; set; }

        // 🔗 FOLLOW-UP ACTIONS
        public string? ResourceConnected { get; set; }
        public bool FollowUpScheduled { get; set; }

        // 🚨 CRISIS TRACKING
        public bool? CrisisProtocolFollowed { get; set; }
        public bool? AuthoritiesContacted { get; set; }
        public bool? VolunteerDebriefed { get; set; }

        // ⏱️ SYSTEM TRACKING
        public DateTime CreatedAt { get; set; }
        public DateTime? NotifiedAt { get; set; }
    }

    public class CreateEscalationDTO
    {
        public string FollowUpId { get; set; }
        public string PersonId { get; set; }
        public string VolunteerId { get; set; }
        public string TeamLeadId { get; set; }

        public string EscalationReason { get; set; }   // NOT "Type"
        public string Description { get; set; }
    }
    public class ResolveEscalationDTO
    {
        public string EscalationId { get; set; }

        public string Status { get; set; } // Resolved / Referred Out / Closed

        public string Notes { get; set; }
        public string Outcome { get; set; }

        public string? ResourceConnected { get; set; }
        public bool FollowUpScheduled { get; set; }

        // Optional crisis fields
        public bool? CrisisProtocolFollowed { get; set; }
        public bool? AuthoritiesContacted { get; set; }
        public bool? VolunteerDebriefed { get; set; }
    }
}