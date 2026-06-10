namespace RM_CMS.DTOs.Worship
{
    public class SongDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public DateTime? LastSungDate { get; set; }
        
        public DateTime? ServiceDate { get; set; }
        public int? ServiceType { get; set; }
        public string? ServiceCategory { get; set; }
    }
}