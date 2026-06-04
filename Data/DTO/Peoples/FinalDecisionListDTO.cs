namespace RM_CMS.Data.DTO.Peoples
{
    public class FinalDecisionListResponseDTO
    {
        public List<FinalDecisionItemDTO> Items { get; set; } = new List<FinalDecisionItemDTO>();
        public int TotalCount { get; set; }
    }

    public class FinalDecisionItemDTO
    {
        public string PersonId { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FinalStatus { get; set; } = string.Empty;

        // 7 Follow-up Steps
        public NurtureStepDTO FollowUp1 { get; set; } = new NurtureStepDTO();
        public NurtureStepDTO FollowUp2 { get; set; } = new NurtureStepDTO();
        public NurtureStepDTO FollowUp3 { get; set; } = new NurtureStepDTO();
        public NurtureStepDTO FollowUp4 { get; set; } = new NurtureStepDTO();
        public NurtureStepDTO FollowUp5 { get; set; } = new NurtureStepDTO();
        public NurtureStepDTO FollowUp6 { get; set; } = new NurtureStepDTO();
        public NurtureStepDTO FollowUp7 { get; set; } = new NurtureStepDTO();
    }

    public class NurtureStepDTO
    {
        public string ResponseType { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public DateTime? ScheduledDate { get; set; }
    }
}
