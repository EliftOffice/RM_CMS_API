namespace RM_CMS.DTOs.Worship
{
    public class RecommendedSongsDto
    {
        public IEnumerable<SongDto> PraiseSongs { get; set; } = new List<SongDto>();
        
        public IEnumerable<SongDto> MidSongs { get; set; } = new List<SongDto>();
        
        public IEnumerable<SongDto> WorshipSongs { get; set; } = new List<SongDto>();
    }
}