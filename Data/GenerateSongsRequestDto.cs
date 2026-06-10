namespace RM_CMS.DTOs.Worship
{
    public class GenerateSongsRequestDto
    {
        public DateTime ServiceDate { get; set; }

        public int ServiceType { get; set; }

        public string ServiceCategory { get; set; } = "NORMAL";
    }
}