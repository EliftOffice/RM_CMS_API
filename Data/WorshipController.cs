using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using RM_CMS.BLL.Worship;
using RM_CMS.DTOs.Worship;
using RM_CMS.DAL.Worship;

namespace RM_CMS.Controllers.Worship
{
    [ApiController]
    [Route("api/[controller]")]
    public class WorshipController : ControllerBase
    {
        private readonly IWorshipService _worshipService;
        private readonly ILogger<WorshipController> _logger;

        public WorshipController(ILogger<WorshipController> logger)
        {
            _logger = logger;
            
            // Hardcoded local connection string as requested
            string localConnectionString = "server=localhost;port=3307;database=worship;user=root;password=Password@123;";
            var repository = new WorshipRepository(localConnectionString);
            _worshipService = new WorshipService(repository);
        }

        [HttpPost("generate")]
        public async Task<IActionResult> GenerateSongs()
        {
            try
            {
                var result = await _worshipService.GenerateSongsAsync();
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while generating worship songs.");
                return StatusCode(500, new { message = "An error occurred while generating the worship songs. Please try again later." });
            }
        }
    }
}