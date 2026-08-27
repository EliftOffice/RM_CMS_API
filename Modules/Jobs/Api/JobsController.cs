using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Modules.Jobs.Domain;
using RM_CMS.Modules.Jobs.Services;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Jobs.Api
{
    /// <summary>
    /// Triggers for the scheduled sweeps.
    ///
    /// There is no in-process scheduler: an external cron calls these endpoints. That
    /// keeps the jobs runnable on demand — a team lead reporting "nobody was told about
    /// my escalation" can have the sweep run and inspected immediately — and means a
    /// second application instance does not silently double every job.
    ///
    /// The <c>JobRunner</c> policy accepts either an Admin token or the scheduler's
    /// <c>X-Service-Key</c>. Note it does NOT require an authenticated user: the
    /// machine caller has no identity, and audit columns are left null rather than
    /// attributed to somebody who was not there.
    /// </summary>
    [ApiController]
    [Route("api/jobs")]
    [Produces("application/json")]
    [Authorize(Policy = PolicyNames.JobRunner)]
    public sealed class JobsController : ControllerBase
    {
        private readonly IJobService _jobs;

        public JobsController(IJobService jobs) => _jobs = jobs;

        /// <summary>Assigns open cases with no volunteer to the least-loaded eligible one.</summary>
        [HttpPost("assign-unassigned")]
        [ProducesResponseType(typeof(ApiResponse<JobReport>), StatusCodes.Status200OK)]
        public async Task<IActionResult> AssignUnassigned([FromQuery] int limit = 0) =>
            Ok(await _jobs.AssignUnassignedAsync(limit));

        /// <summary>Creates the next nurture contact for cases whose step has fallen due.</summary>
        [HttpPost("advance-nurture")]
        [ProducesResponseType(typeof(ApiResponse<JobReport>), StatusCodes.Status200OK)]
        public async Task<IActionResult> AdvanceNurture([FromQuery] int limit = 0) =>
            Ok(await _jobs.AdvanceNurtureAsync(limit));

        /// <summary>Marks pending contacts past their date (plus grace) as missed.</summary>
        [HttpPost("mark-overdue")]
        [ProducesResponseType(typeof(ApiResponse<JobReport>), StatusCodes.Status200OK)]
        public async Task<IActionResult> MarkOverdue([FromQuery] int limit = 0) =>
            Ok(await _jobs.MarkOverdueAsync(limit));

        /// <summary>
        /// Chases escalations nobody has acknowledged — team lead first, then the
        /// pastor once the threshold passes. This is the loop that stops a concern
        /// sitting unseen.
        /// </summary>
        [HttpPost("chase-escalations")]
        [ProducesResponseType(typeof(ApiResponse<JobReport>), StatusCodes.Status200OK)]
        public async Task<IActionResult> ChaseEscalations([FromQuery] int limit = 0) =>
            Ok(await _jobs.ChaseEscalationsAsync(limit));

        /// <summary>
        /// Reminds each team lead and their volunteers that the huddle is today.
        /// Safe to call any day: it does nothing unless today is the configured
        /// huddle day, and it will not remind the same team twice in one day.
        /// </summary>
        [HttpPost("huddle-reminder")]
        [ProducesResponseType(typeof(ApiResponse<JobReport>), StatusCodes.Status200OK)]
        public async Task<IActionResult> HuddleReminder([FromQuery] int limit = 0) =>
            Ok(await _jobs.HuddleReminderAsync(limit));

        /// <summary>
        /// Sends the queued alerts. Safe to call on its own after a Telegram outage —
        /// anything still pending goes out, and anything already sent is not resent.
        /// </summary>
        [HttpPost("send-notifications")]
        [ProducesResponseType(typeof(ApiResponse<JobReport>), StatusCodes.Status200OK)]
        public async Task<IActionResult> SendNotifications([FromQuery] int limit = 0) =>
            Ok(await _jobs.SendNotificationsAsync(limit));

        /// <summary>Runs every job in the order they depend on each other.</summary>
        [HttpPost("run-all")]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<JobReport>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> RunAll([FromQuery] int limit = 0) =>
            Ok(await _jobs.RunAllAsync(limit));

        /// <summary>
        /// Recent runs from the <c>job_run</c> ledger. A run with no finish time
        /// crashed — that is worth seeing, so it is not filtered out.
        /// </summary>
        [HttpGet("history")]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<JobRun>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> History(
            [FromQuery] string? jobName = null,
            [FromQuery] int limit = 25) =>
            Ok(await _jobs.GetHistoryAsync(jobName, limit));
    }
}
