using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Modules.Jobs.Domain;
using RM_CMS.Modules.Jobs.Services;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Jobs.Api
{
    /// <summary>
    /// Triggers for the scheduled sweeps, and the schedule they run on.
    ///
    /// These endpoints stay callable on demand whatever the schedule says — a team
    /// lead reporting "nobody was told about my escalation" can have the sweep run
    /// and inspected immediately, and an external cron can still drive them.
    ///
    /// This comment used to read "There is no in-process scheduler". There is one
    /// now: see <see cref="Services.JobSchedulerHostedService"/>. Its objection — that
    /// a second application instance would silently double every job — is answered in
    /// the database, by a compare-and-swap on <c>job_schedule.next_due_at</c> that
    /// only one instance can win.
    ///
    /// The <c>JobRunner</c> policy accepts either an Admin token or the scheduler's
    /// <c>X-Service-Key</c>. Note it does NOT require an authenticated user: the
    /// machine caller has no identity, and audit columns are left null rather than
    /// attributed to somebody who was not there. The two SCHEDULE actions narrow that
    /// to Admin on top, because a machine key exists to run work, not to decide when
    /// the church's messages go out.
    /// </summary>
    [ApiController]
    [Route("api/jobs")]
    [Produces("application/json")]
    [Authorize(Policy = PolicyNames.JobRunner)]
    public sealed class JobsController : ControllerBase
    {
        private readonly IJobService _jobs;
        private readonly IJobScheduleService _schedule;

        public JobsController(IJobService jobs, IJobScheduleService schedule)
        {
            _jobs = jobs;
            _schedule = schedule;
        }

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

        /// <summary>
        /// Every job, when it is set to run, and when it last did.
        /// </summary>
        /// <remarks>
        /// Admin on top of the controller's JobRunner policy, so the service key that
        /// triggers work cannot read or rewrite the timetable.
        /// </remarks>
        [HttpGet("schedules")]
        [Authorize(Policy = PolicyNames.AdminOnly)]
        [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<JobScheduleDto>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Schedules() =>
            Ok(await _schedule.ListAsync());

        /// <summary>Changes when one job runs.</summary>
        [HttpPut("schedules/{jobName}")]
        [Authorize(Policy = PolicyNames.AdminOnly)]
        [ProducesResponseType(typeof(ApiResponse<JobScheduleDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> UpdateSchedule(
            string jobName, [FromBody] JobScheduleUpdateRequest request) =>
            Ok(await _schedule.UpdateAsync(jobName, request));
    }
}
