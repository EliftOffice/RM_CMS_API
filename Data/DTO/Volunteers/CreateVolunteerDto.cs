using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace RM_CMS.Data.DTO.Volunteers
{
    public class CreateVolunteerDto
    {

        public string VolunteerId { get; set; }
        public string TelegramChatId { get; set; }
        public string FirstName { get; set; } = string.Empty;

        public string LastName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string? Phone { get; set; }
        public string? TeamLead { get; set; }
        public DateTime StartDate { get; set; } = DateTime.Now;
        public string CapacityBand { get; set; } = string.Empty;




        //public string Status { get; set; } = string.Empty;

        //public string Level { get; set; } = string.Empty;



        //public int CapacityMin { get; set; }

        //public int CapacityMax { get; set; }



        //public string? Campus { get; set; } = "Ongole";

        //public DateTime? Level0Complete { get; set; }

        //public DateTime? CrisisTrained { get; set; }

        //public DateTime? ConfidentialitySigned { get; set; }

        //public DateTime? BackgroundCheck { get; set; }
    }

    public class UpdateVolunteerMobileDto
    {
        public string VolunteerId { get; set; }
        public string NewMobile { get; set; }
    }
}
