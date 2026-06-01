using Microsoft.AspNetCore.Mvc;
using RM_CMS.BLL.Nurture;
using RM_CMS.Data.DTO.Nurture;

namespace RM_CMS.Controllers.Nurture
{
    [ApiController]
    [Route("api/nurture")]
    public class NurtureController : ControllerBase
    {
        private readonly INurtureBLL _nurtureBLL;

        public NurtureController(INurtureBLL nurtureBLL)
        {
            _nurtureBLL = nurtureBLL;
        }

        // ── Volunteer: get all nurture steps due today (or overdue)
        // GET /api/nurture/volunteer/{volunteerId}/due
        [HttpGet("volunteer/{volunteerId}/due")]
        public async Task<IActionResult> GetDueSteps(string volunteerId)
        {
            var result = await _nurtureBLL.GetDueStepsForVolunteerAsync(volunteerId);
            return Ok(result);
        }

        // ── Volunteer: log a step outcome
        // POST /api/nurture/step/log
        [HttpPost("step/log")]
        public async Task<IActionResult> LogStep([FromBody] NurtureStepLogDto dto)
        {
            if (string.IsNullOrEmpty(dto.StepId) || string.IsNullOrEmpty(dto.ContactStatus))
                return BadRequest("step_id and contact_status are required");

            var result = await _nurtureBLL.LogStepAsync(dto);
            return Ok(result);
        }

        // ── Team Lead: active sequences for dashboard + huddle
        // GET /api/nurture/teamlead/{teamLeadId}/active
        [HttpGet("teamlead/{teamLeadId}/active")]
        public async Task<IActionResult> GetActiveSequences(string teamLeadId)
        {
            var result = await _nurtureBLL.GetActiveSequencesForTeamLeadAsync(teamLeadId);
            return Ok(result);
        }

        // ── Team Lead: sequences awaiting final decision (step 7 done)
        // GET /api/nurture/teamlead/{teamLeadId}/review
        [HttpGet("teamlead/{teamLeadId}/review")]
        public async Task<IActionResult> GetAwaitingReview(string teamLeadId)
        {
            var result = await _nurtureBLL.GetSequencesAwaitingReviewAsync(teamLeadId);
            return Ok(result);
        }

        // ── Team Lead: close sequence as Permanent or Failed
        // POST /api/nurture/sequence/close
        [HttpPost("sequence/close")]
        public async Task<IActionResult> CloseSequence([FromBody] CloseSequenceDto dto)
        {
            if (string.IsNullOrEmpty(dto.SequenceId) || string.IsNullOrEmpty(dto.FinalStatus))
                return BadRequest("sequence_id and final_status are required");

            if (dto.FinalStatus != "Permanent" && dto.FinalStatus != "Failed")
                return BadRequest("final_status must be 'Permanent' or 'Failed'");

            var result = await _nurtureBLL.CloseSequenceAsync(dto);
            return Ok(result);
        }

        // ── Get all 7 steps for a sequence (history / audit view)
        // GET /api/nurture/sequence/{sequenceId}/steps
        [HttpGet("sequence/{sequenceId}/steps")]
        public async Task<IActionResult> GetSteps(string sequenceId)
        {
            var result = await _nurtureBLL.GetStepsBySequenceAsync(sequenceId);
            return Ok(result);
        }
    }
}
