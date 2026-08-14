using RM_CMS.Modules.Care.Api;
using RM_CMS.Modules.Care.Data;
using RM_CMS.Modules.Care.Domain;
using RM_CMS.Modules.Identity.Data;
using RM_CMS.Modules.Identity.Domain;
using RM_CMS.Modules.Identity.Services;
using RM_CMS.Modules.Volunteers.Data;
using RM_CMS.Utilities;

namespace RM_CMS.Modules.Care.Services
{
    public interface ICareService
    {
        Task<ApiResponse<PagedResult<CaseSummaryDto>>> SearchAsync(
            int page, int pageSize, string? search, string? stage, string? status, string? volunteerId, bool? unassigned);

        Task<ApiResponse<CaseDto>> GetAsync(string publicId);
        Task<ApiResponse<CaseDto>> OpenAsync(OpenCaseRequest request);
        Task<ApiResponse<CaseDto>> AssignAsync(string publicId, AssignCaseRequest request);

        /// <summary>The main funnel: log a contact and let the rules decide what follows.</summary>
        Task<ApiResponse<InteractionResultDto>> LogInteractionAsync(string interactionId, LogInteractionRequest request);

        Task<ApiResponse<IReadOnlyList<InteractionDto>>> GetMyWorkListAsync();

        Task<ApiResponse<CaseDto>> CompleteReviewAsync(string publicId, ReviewDecisionRequest request);

        // ---- escalations ----
        Task<ApiResponse<PagedResult<EscalationDto>>> SearchEscalationsAsync(int page, int pageSize, string? status, bool mineOnly);
        Task<ApiResponse<EscalationDto>> RaiseEscalationAsync(string casePublicId, RaiseEscalationRequest request);
        Task<ApiResponse<EscalationDto>> AcknowledgeAsync(string escalationId);
        Task<ApiResponse<EscalationDto>> ResolveAsync(string escalationId, ResolveEscalationRequest request);

        // ---- notes ----
        Task<ApiResponse<NoteDto>> AddNoteAsync(string entityType, string entityPublicId, AddNoteRequest request);
    }

    public sealed class CareService : ICareService
    {
        private const string CaseNotFound = "Case not found.";

        private readonly ICareCaseRepository _cases;
        private readonly ICareInteractionRepository _interactions;
        private readonly IEscalationRepository _escalations;
        private readonly ICareLookupRepository _lookups;
        private readonly IProgressionEngine _engine;
        private readonly IVolunteerRepository _volunteers;
        private readonly ICurrentIdentity _current;
        private readonly IUserAccountRepository _accounts;
        private readonly TimeProvider _clock;
        private readonly ILogger<CareService> _logger;

        public CareService(
            ICareCaseRepository cases,
            ICareInteractionRepository interactions,
            IEscalationRepository escalations,
            ICareLookupRepository lookups,
            IProgressionEngine engine,
            IVolunteerRepository volunteers,
            ICurrentIdentity current,
            IUserAccountRepository accounts,
            TimeProvider clock,
            ILogger<CareService> logger)
        {
            _cases = cases;
            _interactions = interactions;
            _escalations = escalations;
            _lookups = lookups;
            _engine = engine;
            _volunteers = volunteers;
            _current = current;
            _accounts = accounts;
            _clock = clock;
            _logger = logger;
        }

        // ==================================================================
        // Reads
        // ==================================================================

        public async Task<ApiResponse<PagedResult<CaseSummaryDto>>> SearchAsync(
            int page, int pageSize, string? search, string? stage, string? status, string? volunteerId, bool? unassigned)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            if (stage is not null && !CaseStage.IsKnown(stage))
                return Warn<PagedResult<CaseSummaryDto>>($"Unknown stage '{stage}'.");

            if (status is not null && !CaseStatus.IsKnown(status))
                return Warn<PagedResult<CaseSummaryDto>>($"Unknown status '{status}'.");

            var campusKey = await _cases.ResolveCampusIdAsync(_current.CampusId);

            long? volunteerKey = null;

            if (!string.IsNullOrWhiteSpace(volunteerId))
            {
                volunteerKey = await _cases.ResolveVolunteerIdAsync(volunteerId);

                if (volunteerKey is null)
                    return Warn<PagedResult<CaseSummaryDto>>("Unknown volunteer.");
            }

            // A volunteer sees only their own cases, whatever they ask for.
            if (!_current.IsAdmin && !_current.IsInRole(RoleCodes.Pastor) && !_current.IsInRole(RoleCodes.TeamLead))
            {
                var mine = _current.VolunteerId;

                if (string.IsNullOrWhiteSpace(mine))
                    return Warn<PagedResult<CaseSummaryDto>>("Your account is not linked to a volunteer record.");

                volunteerKey = await _cases.ResolveVolunteerIdAsync(mine);
            }

            var query = new CaseQuery
            {
                Search = search,
                Stage = stage,
                Status = status,
                CampusId = campusKey,
                VolunteerId = volunteerKey,
                Unassigned = unassigned,
                Skip = (page - 1) * pageSize,
                Take = pageSize
            };

            var total = await _cases.CountAsync(query);
            var rows = await _cases.SearchAsync(query);

            return Ok(new PagedResult<CaseSummaryDto>
            {
                Items = rows.Select(ToSummary).ToList(),
                Page = page,
                PageSize = pageSize,
                TotalCount = total
            }, "Cases retrieved");
        }

        public async Task<ApiResponse<CaseDto>> GetAsync(string publicId)
        {
            var careCase = await _cases.GetByPublicIdAsync(publicId);

            if (careCase is null || !CanAccess(careCase))
                return Warn<CaseDto>(CaseNotFound);

            return Ok(await BuildDetailAsync(careCase), "Case retrieved");
        }

        public async Task<ApiResponse<IReadOnlyList<InteractionDto>>> GetMyWorkListAsync()
        {
            var volunteerPublicId = _current.VolunteerId;

            if (string.IsNullOrWhiteSpace(volunteerPublicId))
                return Warn<IReadOnlyList<InteractionDto>>("Your account is not linked to a volunteer record.");

            var volunteerKey = await _cases.ResolveVolunteerIdAsync(volunteerPublicId);

            if (volunteerKey is null)
                return Warn<IReadOnlyList<InteractionDto>>("Volunteer record not found.");

            var today = _clock.GetUtcNow().UtcDateTime.Date;
            var due = await _interactions.GetDueForVolunteerAsync(volunteerKey.Value, today);

            var items = new List<InteractionDto>();

            foreach (var i in due)
            {
                var c = await _cases.GetByIdAsync(i.CareCaseId);
                items.Add(ToDto(i, today, c));
            }

            return Ok<IReadOnlyList<InteractionDto>>(items,
                items.Count == 0 ? "Nothing is due today." : $"{items.Count} contact(s) due.");
        }

        // ==================================================================
        // Open and assign
        // ==================================================================

        public async Task<ApiResponse<CaseDto>> OpenAsync(OpenCaseRequest request)
        {
            var personKey = await _cases.ResolvePersonIdAsync(request.PersonId);

            if (personKey is null)
                return Warn<CaseDto>("That person does not exist.");

            var existing = await _cases.FindOpenCaseForPersonAsync(personKey.Value);

            if (existing is not null)
            {
                return Warn<CaseDto>(
                    $"{existing.PersonName} already has an open case ({existing.ReferenceCode}). " +
                    "Close it before opening another.");
            }

            // Consent, checked BEFORE anything is written. Someone who asked not to be
            // contacted must not be pulled into a new journey just because a
            // well-meaning operator recorded them again.
            if (await _cases.IsPersonDoNotContactAsync(personKey.Value))
            {
                _logger.LogWarning(
                    "Refused to open a case for person {Person}: do-not-contact is set", request.PersonId);

                return Warn<CaseDto>(
                    "This person has asked not to be contacted, so no case can be opened for them. " +
                    "A team lead can lift that on their record if it was recorded in error.");
            }

            var campusKey = await _cases.ResolveCampusIdAsync(_current.CampusId);

            if (campusKey is null)
                return Warn<CaseDto>("A campus is required to open a case.");

            var plan = await _lookups.GetDefaultPlanAsync(campusKey);

            var careCase = new CareCase
            {
                PublicId = Ulid.NewUlid(),
                PersonId = personKey.Value,
                CampusId = campusKey.Value,
                Stage = CaseStage.Intake,
                Status = CaseStatus.AwaitingAssignment,
                Priority = CasePriority.IsKnown(request.Priority) ? request.Priority ?? CasePriority.Normal : CasePriority.Normal,
                VisitType = request.VisitType,
                ConnectionSourceCode = request.ConnectionSource,
                FirstVisitOn = request.FirstVisitOn,
                NurturePlanId = plan?.Id
            };

            var actingUserId = await ActingUserIdAsync();
            var id = await _cases.OpenAsync(careCase, actingUserId);

            var created = await _cases.GetByIdAsync(id);

            if (created is null)
                return Fail<CaseDto>("The case was opened but could not be read back.");

            var message = $"Case {created.ReferenceCode} opened.";

            if (request.AutoAssign)
            {
                var assigned = await AutoAssignAsync(created, actingUserId);

                if (assigned is not null)
                {
                    created = assigned;
                    message += $" Assigned to {created.AssignedVolunteerName}.";
                }
                else
                {
                    message += " No volunteer has spare capacity, so it is queued for assignment.";
                }
            }

            _logger.LogInformation("Case {Case} opened for person {Person}", created.PublicId, request.PersonId);

            return Ok(await BuildDetailAsync(created), message);
        }

        /// <summary>
        /// Picks the least-loaded eligible volunteer and assigns. Returns null when
        /// nobody has capacity — the case stays queued rather than overloading anyone.
        /// </summary>
        private async Task<CareCase?> AutoAssignAsync(CareCase careCase, long? actingUserId)
        {
            var eligible = await _volunteers.FindEligibleAsync(careCase.CampusId, crisisCapable: false, limit: 1);
            var pick = eligible.FirstOrDefault();

            if (pick is null) return null;

            var ok = await _cases.AssignAsync(
                careCase.Id, careCase.RowVersion, pick.Id, pick.TeamId,
                AssignmentReason.Auto, _clock.GetUtcNow().UtcDateTime, actingUserId);

            if (!ok) return null;

            var refreshed = await _cases.GetByIdAsync(careCase.Id);

            if (refreshed is not null)
                await CreateFirstFollowUpAsync(refreshed, pick.Id, actingUserId);

            return await _cases.GetByIdAsync(careCase.Id);
        }

        private async Task CreateFirstFollowUpAsync(CareCase careCase, long volunteerId, long? actingUserId)
        {
            var existing = await _interactions.GetPendingForCaseAsync(careCase.Id);

            if (existing is not null) return;

            var targetHours = await _lookups.GetIntSettingAsync("assignment.response_target_hours", 48);

            await _interactions.CreateAsync(new CareInteraction
            {
                PublicId = Ulid.NewUlid(),
                CareCaseId = careCase.Id,
                VolunteerId = volunteerId,
                Stage = InteractionStage.InitialFollowUp,
                SequenceNumber = await _interactions.MaxSequenceAsync(careCase.Id, InteractionStage.InitialFollowUp) + 1,
                MethodCode = "CALL",
                ScheduledOn = _clock.GetUtcNow().UtcDateTime.Date.AddDays(Math.Max(0, targetHours / 24)),
                Status = InteractionStatus.Pending
            }, actingUserId);
        }

        public async Task<ApiResponse<CaseDto>> AssignAsync(string publicId, AssignCaseRequest request)
        {
            var careCase = await _cases.GetByPublicIdAsync(publicId);

            if (careCase is null || !CanAccess(careCase))
                return Warn<CaseDto>(CaseNotFound);

            if (!careCase.IsOpen)
                return Warn<CaseDto>("This case is closed.");

            if (careCase.PersonDoNotContact)
                return Warn<CaseDto>($"{careCase.PersonName} has asked not to be contacted. This case cannot be assigned.");

            var volunteerKey = await _cases.ResolveVolunteerIdAsync(request.VolunteerId);

            if (volunteerKey is null)
                return Warn<CaseDto>("Unknown volunteer.");

            var volunteer = await _volunteers.GetByIdAsync(volunteerKey.Value);

            if (volunteer is null || !volunteer.IsAvailable)
                return Warn<CaseDto>("That volunteer is not currently active.");

            if (volunteer.CampusId != careCase.CampusId)
                return Warn<CaseDto>("That volunteer serves a different campus.");

            if (!AssignmentReason.IsKnown(request.Reason))
                return Warn<CaseDto>($"Unknown assignment reason '{request.Reason}'.");

            // Over-capacity assignment is allowed but flagged: a team lead sometimes
            // has to place a case anyway, and hiding it would be worse than saying so.
            var overCapacity = !volunteer.HasSpareCapacity;

            var actingUserId = await ActingUserIdAsync();

            var ok = await _cases.AssignAsync(
                careCase.Id, request.RowVersion, volunteer.Id, volunteer.TeamId,
                request.Reason ?? AssignmentReason.Manual, _clock.GetUtcNow().UtcDateTime, actingUserId);

            if (!ok)
                return Warn<CaseDto>("This case was changed by someone else. Reload and try again.");

            // A handover note is how the incoming volunteer learns the context.
            if (!string.IsNullOrWhiteSpace(request.HandoverNote))
            {
                await _cases.AddNoteAsync(new CareNote
                {
                    PublicId = Ulid.NewUlid(),
                    EntityType = NoteEntityTypes.CareCase,
                    EntityId = careCase.Id,
                    NoteTypeCode = "FOLLOW_UP",
                    Body = request.HandoverNote.Trim()
                }, actingUserId);
            }

            var refreshed = await _cases.GetByIdAsync(careCase.Id);
            await CreateFirstFollowUpAsync(refreshed!, volunteer.Id, actingUserId);

            _logger.LogInformation(
                "Case {Case} assigned to volunteer {Volunteer} ({Reason})",
                publicId, volunteer.PublicId, request.Reason ?? AssignmentReason.Manual);

            var final = await _cases.GetByIdAsync(careCase.Id);

            var message = $"Assigned to {volunteer.FullName}."
                + (overCapacity
                    ? $" Note they are already at {volunteer.CurrentCaseLoad} of {volunteer.CapacityMaxPerWeek} cases."
                    : "");

            return Ok(await BuildDetailAsync(final!), message);
        }

        // ==================================================================
        // The main funnel
        // ==================================================================

        public async Task<ApiResponse<InteractionResultDto>> LogInteractionAsync(
            string interactionId, LogInteractionRequest request)
        {
            var interaction = await _interactions.GetByPublicIdAsync(interactionId);

            if (interaction is null)
                return Warn<InteractionResultDto>("Contact not found.");

            var careCase = await _cases.GetByIdAsync(interaction.CareCaseId);

            if (careCase is null || !CanAccess(careCase))
                return Warn<InteractionResultDto>(CaseNotFound);

            if (!careCase.IsOpen)
                return Warn<InteractionResultDto>("This case is closed.");

            if (!interaction.IsPending)
                return Warn<InteractionResultDto>("This contact has already been logged.");

            // A volunteer may only log their own contacts.
            if (!_current.CanActForVolunteer(interaction.VolunteerPublicId))
                return Warn<InteractionResultDto>("This contact is assigned to another volunteer.");

            if (!await _lookups.OutcomeExistsAsync(request.OutcomeCode))
                return Warn<InteractionResultDto>($"Unknown outcome '{request.OutcomeCode}'.");

            if (!string.IsNullOrWhiteSpace(request.IntentCode) &&
                !await _lookups.IntentExistsAsync(request.IntentCode))
            {
                return Warn<InteractionResultDto>($"Unknown intent '{request.IntentCode}'.");
            }

            var behaviour = await _lookups.GetOutcomeBehaviourAsync(request.OutcomeCode);
            var now = _clock.GetUtcNow().UtcDateTime;
            var occurredAt = request.OccurredAt ?? now;

            if (occurredAt > now.AddMinutes(5))
                return Warn<InteractionResultDto>("A contact cannot be logged in the future.");

            var actingUserId = await ActingUserIdAsync();
            var volunteerKey = interaction.VolunteerId;

            var completed = await _interactions.CompleteAsync(
                interaction.Id, request.RowVersion, occurredAt,
                behaviour?.ContactMade ?? true, request.OutcomeCode, request.IntentCode,
                request.DurationMinutes, request.Notes?.Trim(), volunteerKey, actingUserId);

            if (!completed)
                return Warn<InteractionResultDto>("This contact was already logged, or changed by someone else.");

            // Consent travels with the person, not the case.
            if (await _lookups.IntentImpliesDoNotContactAsync(request.IntentCode))
            {
                await _cases.SetPersonDoNotContactAsync(
                    careCase.PersonId,
                    $"Requested during contact on {occurredAt:d MMM yyyy}.", now, actingUserId);

                _logger.LogWarning(
                    "Do-not-contact recorded for person {Person} from case {Case}",
                    careCase.PersonPublicId, careCase.PublicId);
            }

            // Reload the completed interaction so the engine sees the stored values.
            var logged = await _interactions.GetByIdAsync(interaction.Id);

            var decision = await _engine.DecideAsync(careCase, logged!, now);

            var result = new InteractionResultDto
            {
                Action = decision.Action,
                Explanation = decision.Explanation,
                MatchedRule = decision.MatchedRule,
                NeedsHumanDecision = decision.NeedsHumanDecision
            };

            // ---- side effects ----
            if (decision.RaiseEscalation)
            {
                var escalationId = await RaiseAutomaticEscalationAsync(careCase, logged!, behaviour?.DefaultTier, actingUserId);
                result.EscalationId = escalationId;
            }
            else if (decision.CloseCase)
            {
                await _cases.CloseAsync(careCase.Id, careCase.RowVersion,
                    decision.CloseReason ?? CaseCloseReason.Other, null, now, actingUserId);

                result.CaseClosed = true;
                result.CloseReason = decision.CloseReason;
            }
            else
            {
                await _cases.UpdateStateAsync(careCase, actingUserId);

                if (decision.CreateStepNumber.HasValue)
                {
                    await CreateNextStepAsync(careCase, decision, actingUserId);
                    result.NextStepNumber = decision.CreateStepNumber;
                    result.NextContactDue = decision.CreateStepDueOn;
                }
                else if (decision.CreateRetryOn.HasValue)
                {
                    await CreateRetryAsync(careCase, decision.CreateRetryOn.Value, actingUserId);
                    result.NextContactDue = decision.CreateRetryOn;
                }
            }

            var final = await _cases.GetByIdAsync(careCase.Id);
            result.Case = await BuildDetailAsync(final!);

            return Ok(result, decision.Explanation);
        }

        private async Task CreateNextStepAsync(CareCase careCase, ProgressionDecision decision, long? actingUserId)
        {
            // Who runs the next step depends on the plan's assignment mode.
            var volunteerId = careCase.AssignedVolunteerId;

            var plan = careCase.NurturePlanId.HasValue
                ? await _lookups.GetPlanByIdAsync(careCase.NurturePlanId.Value)
                : null;

            if (plan?.AssignmentMode is NurtureAssignmentMode.ReassignLeastLoaded ||
                (plan?.AssignmentMode is NurtureAssignmentMode.SameIfAvailable && !await IsStillAvailableAsync(volunteerId)))
            {
                var eligible = await _volunteers.FindEligibleAsync(careCase.CampusId, false, 1);
                var pick = eligible.FirstOrDefault();

                if (pick is not null && pick.Id != volunteerId)
                {
                    await _cases.AssignAsync(careCase.Id, careCase.RowVersion + 1, pick.Id, pick.TeamId,
                        AssignmentReason.Capacity, _clock.GetUtcNow().UtcDateTime, actingUserId);

                    volunteerId = pick.Id;
                }
            }

            await _interactions.CreateAsync(new CareInteraction
            {
                PublicId = Ulid.NewUlid(),
                CareCaseId = careCase.Id,
                VolunteerId = volunteerId,
                Stage = InteractionStage.Nurture,
                SequenceNumber = decision.CreateStepNumber!.Value,
                MethodCode = decision.CreateStepMethod,
                ScheduledOn = decision.CreateStepDueOn,
                Status = InteractionStatus.Pending
            }, actingUserId);
        }

        private async Task<bool> IsStillAvailableAsync(long? volunteerId)
        {
            if (volunteerId is null) return false;

            var volunteer = await _volunteers.GetByIdAsync(volunteerId.Value);
            return volunteer is not null && volunteer.IsAvailable && volunteer.HasSpareCapacity;
        }

        private async Task CreateRetryAsync(CareCase careCase, DateTime dueOn, long? actingUserId)
        {
            await _interactions.CreateAsync(new CareInteraction
            {
                PublicId = Ulid.NewUlid(),
                CareCaseId = careCase.Id,
                VolunteerId = careCase.AssignedVolunteerId,
                Stage = InteractionStage.InitialFollowUp,
                SequenceNumber = await _interactions.MaxSequenceAsync(careCase.Id, InteractionStage.InitialFollowUp) + 1,
                MethodCode = "CALL",
                ScheduledOn = dueOn,
                Status = InteractionStatus.Pending
            }, actingUserId);
        }

        private async Task<string?> RaiseAutomaticEscalationAsync(
            CareCase careCase, CareInteraction interaction, string? defaultTier, long? actingUserId)
        {
            var escalation = new Escalation
            {
                PublicId = Ulid.NewUlid(),
                CareCaseId = careCase.Id,
                CareInteractionId = interaction.Id,
                RaisedByVolunteerId = interaction.VolunteerId,
                CampusId = careCase.CampusId,
                ReasonCode = "GENERAL_CONCERN",
                Tier = defaultTier ?? EscalationTier.Standard,
                Description =
                    $"Raised automatically from a {interaction.OutcomeLabel ?? interaction.OutcomeCode} outcome. " +
                    (string.IsNullOrWhiteSpace(interaction.Notes) ? "No notes recorded." : interaction.Notes),
                RaisedAt = _clock.GetUtcNow().UtcDateTime
            };

            var id = await _escalations.RaiseAndPauseCaseAsync(escalation, careCase.RowVersion, actingUserId);

            if (id == 0)
            {
                _logger.LogError("Failed to raise escalation for case {Case}", careCase.PublicId);
                return null;
            }

            _logger.LogWarning(
                "Escalation raised on case {Case} at tier {Tier}; case paused",
                careCase.PublicId, escalation.Tier);

            return escalation.PublicId;
        }

        // ==================================================================
        // Review
        // ==================================================================

        public async Task<ApiResponse<CaseDto>> CompleteReviewAsync(string publicId, ReviewDecisionRequest request)
        {
            var careCase = await _cases.GetByPublicIdAsync(publicId);

            if (careCase is null || !CanAccess(careCase))
                return Warn<CaseDto>(CaseNotFound);

            if (!careCase.IsOpen)
                return Warn<CaseDto>("This case is already closed.");

            if (!CaseCloseReason.IsKnown(request.CloseReason))
                return Warn<CaseDto>($"Unknown close reason '{request.CloseReason}'.");

            var now = _clock.GetUtcNow().UtcDateTime;
            var actingUserId = await ActingUserIdAsync();

            var closed = await _cases.CloseAsync(
                careCase.Id, request.RowVersion, request.CloseReason, request.Notes?.Trim(), now, actingUserId);

            if (!closed)
                return Warn<CaseDto>("This case was changed by someone else. Reload and try again.");

            _logger.LogInformation(
                "Case {Case} closed as {Reason} by {Account}",
                publicId, request.CloseReason, _current.AccountId);

            var final = await _cases.GetByIdAsync(careCase.Id);

            return Ok(await BuildDetailAsync(final!),
                request.CloseReason == CaseCloseReason.BecameMember
                    ? $"{careCase.PersonName} is now recorded as a member."
                    : $"Case closed ({request.CloseReason.Replace('_', ' ').ToLowerInvariant()}).");
        }

        // ==================================================================
        // Escalations
        // ==================================================================

        public async Task<ApiResponse<PagedResult<EscalationDto>>> SearchEscalationsAsync(
            int page, int pageSize, string? status, bool mineOnly)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            if (status is not null && !EscalationStatus.IsKnown(status))
                return Warn<PagedResult<EscalationDto>>($"Unknown status '{status}'.");

            var campusKey = await _cases.ResolveCampusIdAsync(_current.CampusId);
            long? assignedTo = null;

            if (mineOnly)
            {
                var account = string.IsNullOrWhiteSpace(_current.AccountId)
                    ? null
                    : await _accounts.GetByPublicIdAsync(_current.AccountId);

                assignedTo = account?.Id;
            }

            var total = await _escalations.CountAsync(campusKey, assignedTo, status);
            var rows = await _escalations.SearchAsync(campusKey, assignedTo, status, (page - 1) * pageSize, pageSize);

            var now = _clock.GetUtcNow().UtcDateTime;

            return Ok(new PagedResult<EscalationDto>
            {
                Items = rows.Select(e => ToDto(e, now)).ToList(),
                Page = page,
                PageSize = pageSize,
                TotalCount = total
            }, "Escalations retrieved");
        }

        public async Task<ApiResponse<EscalationDto>> RaiseEscalationAsync(string casePublicId, RaiseEscalationRequest request)
        {
            var careCase = await _cases.GetByPublicIdAsync(casePublicId);

            if (careCase is null || !CanAccess(careCase))
                return Warn<EscalationDto>(CaseNotFound);

            if (!careCase.IsOpen)
                return Warn<EscalationDto>("This case is closed.");

            if (!await _escalations.ReasonExistsAsync(request.ReasonCode))
                return Warn<EscalationDto>($"Unknown escalation reason '{request.ReasonCode}'.");

            if (!EscalationTier.IsKnown(request.Tier))
                return Warn<EscalationDto>($"Unknown tier '{request.Tier}'.");

            var now = _clock.GetUtcNow().UtcDateTime;
            var actingUserId = await ActingUserIdAsync();

            var escalation = new Escalation
            {
                PublicId = Ulid.NewUlid(),
                CareCaseId = careCase.Id,
                RaisedByVolunteerId = careCase.AssignedVolunteerId,
                CampusId = careCase.CampusId,
                ReasonCode = request.ReasonCode,
                Tier = request.Tier ?? EscalationTier.Standard,
                Description = request.Description.Trim(),
                RaisedAt = now
            };

            var id = await _escalations.RaiseAndPauseCaseAsync(escalation, request.CaseRowVersion, actingUserId);

            if (id == 0)
                return Warn<EscalationDto>("This case was changed by someone else. Reload and try again.");

            _logger.LogWarning(
                "Escalation {Escalation} raised on case {Case} ({Reason}, {Tier}); case paused",
                escalation.PublicId, casePublicId, request.ReasonCode, escalation.Tier);

            var saved = await _escalations.GetByPublicIdAsync(escalation.PublicId);

            return Ok(ToDto(saved!, now),
                "Escalation raised. Follow-up on this case is paused until it is resolved.");
        }

        public async Task<ApiResponse<EscalationDto>> AcknowledgeAsync(string escalationId)
        {
            var escalation = await _escalations.GetByPublicIdAsync(escalationId);

            if (escalation is null || !_current.CanAccessCampus(escalation.CampusPublicId))
                return Warn<EscalationDto>("Escalation not found.");

            if (escalation.IsAcknowledged)
                return Warn<EscalationDto>($"Already acknowledged by {escalation.AssignedToName ?? "someone"}.");

            var account = string.IsNullOrWhiteSpace(_current.AccountId)
                ? null
                : await _accounts.GetByPublicIdAsync(_current.AccountId);

            if (account is null)
                return Warn<EscalationDto>("Your account could not be resolved.");

            var now = _clock.GetUtcNow().UtcDateTime;
            var ok = await _escalations.AcknowledgeAsync(escalation.Id, escalation.RowVersion, account.Id, now);

            if (!ok)
                return Warn<EscalationDto>("This escalation was changed by someone else. Reload and try again.");

            _logger.LogInformation("Escalation {Escalation} acknowledged by {Account}", escalationId, _current.AccountId);

            var saved = await _escalations.GetByPublicIdAsync(escalationId);
            return Ok(ToDto(saved!, now), "Acknowledged. The case stays paused until you resolve it.");
        }

        public async Task<ApiResponse<EscalationDto>> ResolveAsync(string escalationId, ResolveEscalationRequest request)
        {
            var escalation = await _escalations.GetByPublicIdAsync(escalationId);

            if (escalation is null || !_current.CanAccessCampus(escalation.CampusPublicId))
                return Warn<EscalationDto>("Escalation not found.");

            if (!escalation.IsOpen)
                return Warn<EscalationDto>("This escalation is already resolved.");

            if (!await _escalations.OutcomeExistsAsync(request.OutcomeCode))
                return Warn<EscalationDto>($"Unknown outcome '{request.OutcomeCode}'.");

            // Safeguarding reasons require the protocol question to be answered — a
            // blank here would leave no record that the process was followed.
            if (escalation.ReasonRequiresProtocol && request.ProtocolFollowed is null)
            {
                return Warn<EscalationDto>(
                    $"'{escalation.ReasonLabel}' requires a documented protocol. " +
                    "Record whether it was followed before resolving.");
            }

            var status = request.Status ?? EscalationStatus.Resolved;

            if (!EscalationStatus.IsTerminal(status))
                return Warn<EscalationDto>($"'{status}' does not close an escalation.");

            var now = _clock.GetUtcNow().UtcDateTime;
            var plan = await _lookups.GetDefaultPlanAsync(escalation.CampusId);
            var resumeDays = request.ResumeInDays ?? plan?.DefaultGapDays ?? 7;

            var ok = await _escalations.ResolveAndResumeCaseAsync(
                escalation.Id, request.RowVersion, status, request.OutcomeCode,
                request.ResolutionNotes?.Trim(), request.ResourceConnected?.Trim(),
                request.ProtocolFollowed, request.AuthoritiesContacted, request.VolunteerDebriefed,
                now, now.Date.AddDays(resumeDays), await ActingUserIdAsync());

            if (!ok)
                return Warn<EscalationDto>("This escalation was changed by someone else. Reload and try again.");

            _logger.LogWarning(
                "Escalation {Escalation} resolved as {Outcome} by {Account}",
                escalationId, request.OutcomeCode, _current.AccountId);

            var saved = await _escalations.GetByPublicIdAsync(escalationId);
            var careCase = await _cases.GetByIdAsync(escalation.CareCaseId);

            // Resuming has to put actual WORK back on the case, not just a date.
            //
            // Setting next_step_due_on alone is not enough: the scheduler only sweeps
            // cases at stage NURTURE, so a case escalated during its initial
            // follow-up would resume with no pending contact and no sweep that could
            // ever create one. It would sit "in progress" forever with nobody
            // noticing — exactly the silent stall this system exists to prevent.
            if (careCase is not null && careCase.IsOpen && !careCase.IsPaused)
            {
                var pending = await _interactions.GetPendingForCaseAsync(careCase.Id);

                if (pending is null && careCase.AssignedVolunteerId.HasValue)
                {
                    var resumeOn = now.Date.AddDays(resumeDays);

                    var stage = careCase.Stage == CaseStage.Nurture
                        ? InteractionStage.Nurture
                        : InteractionStage.InitialFollowUp;

                    await _interactions.CreateAsync(new CareInteraction
                    {
                        PublicId = Ulid.NewUlid(),
                        CareCaseId = careCase.Id,
                        VolunteerId = careCase.AssignedVolunteerId,
                        Stage = stage,
                        SequenceNumber = await _interactions.MaxSequenceAsync(careCase.Id, stage) + 1,
                        MethodCode = "CALL",
                        ScheduledOn = resumeOn,
                        Status = InteractionStatus.Pending
                    }, await ActingUserIdAsync());

                    _logger.LogInformation(
                        "Case {Case} resumed after escalation {Escalation}; next contact due {Due:d MMM yyyy}",
                        careCase.PublicId, escalationId, resumeOn);
                }
            }

            var message = careCase?.IsPaused == true
                ? "Resolved. The case stays paused — another escalation is still open."
                : $"Resolved. The next contact is due in {resumeDays} days.";

            return Ok(ToDto(saved!, now), message);
        }

        // ==================================================================
        // Notes
        // ==================================================================

        public async Task<ApiResponse<NoteDto>> AddNoteAsync(string entityType, string entityPublicId, AddNoteRequest request)
        {
            var type = entityType.Trim().ToUpperInvariant();

            if (!NoteEntityTypes.IsKnown(type))
                return Warn<NoteDto>($"Unknown entity type '{entityType}'.");

            if (request.IsPrivate && string.IsNullOrWhiteSpace(request.VisibleToRole))
                return Warn<NoteDto>("A private note must name the role that may read it.");

            long? entityKey = type switch
            {
                NoteEntityTypes.CareCase => (await _cases.GetByPublicIdAsync(entityPublicId))?.Id,
                NoteEntityTypes.Person   => await _cases.ResolvePersonIdAsync(entityPublicId),
                NoteEntityTypes.Volunteer => await _cases.ResolveVolunteerIdAsync(entityPublicId),
                _ => null
            };

            if (entityKey is null)
                return Warn<NoteDto>("That record does not exist.");

            var note = new CareNote
            {
                PublicId = Ulid.NewUlid(),
                EntityType = type,
                EntityId = entityKey.Value,
                NoteTypeCode = string.IsNullOrWhiteSpace(request.NoteType) ? "GENERAL" : request.NoteType.Trim(),
                Body = request.Body.Trim(),
                Tags = request.Tags?.Trim(),
                IsPrivate = request.IsPrivate,
                VisibleToRoleCode = request.IsPrivate ? request.VisibleToRole : null
            };

            await _cases.AddNoteAsync(note, await ActingUserIdAsync());

            return Ok(new NoteDto
            {
                Id = note.PublicId,
                NoteType = note.NoteTypeCode,
                Body = note.Body,
                Tags = note.Tags,
                IsPrivate = note.IsPrivate,
                VisibleToRole = note.VisibleToRoleCode,
                CreatedAt = _clock.GetUtcNow().UtcDateTime,
                CreatedBy = _current.DisplayName
            }, "Note added");
        }

        // ==================================================================
        // Helpers
        // ==================================================================

        private bool CanAccess(CareCase c) => _current.CanAccessCampus(c.CampusPublicId);

        private async Task<long?> ActingUserIdAsync()
        {
            if (string.IsNullOrWhiteSpace(_current.AccountId)) return null;

            var account = await _accounts.GetByPublicIdAsync(_current.AccountId);
            return account?.Id;
        }

        private async Task<CaseDto> BuildDetailAsync(CareCase c)
        {
            var dto = ToDto(c);
            var today = _clock.GetUtcNow().UtcDateTime.Date;
            var now = _clock.GetUtcNow().UtcDateTime;

            var interactions = await _interactions.GetForCaseAsync(c.Id);
            dto.Interactions = interactions.Select(i => ToDto(i, today, null)).ToList();

            var escalations = await _escalations.GetForCaseAsync(c.Id);
            dto.Escalations = escalations.Select(e => ToDto(e, now)).ToList();

            // Private notes are filtered in SQL, so a volunteer never receives one
            // intended for a team lead.
            var canSeePrivate = _current.IsAdmin
                || _current.IsInRole(RoleCodes.Pastor)
                || _current.IsInRole(RoleCodes.TeamLead);

            var notes = await _cases.GetNotesAsync(NoteEntityTypes.CareCase, c.Id, canSeePrivate);
            dto.Notes = notes.Select(ToDto).ToList();

            var history = await _cases.GetAssignmentHistoryAsync(c.Id);
            dto.AssignmentHistory = history.Select(a => new AssignmentDto
            {
                VolunteerName = a.VolunteerName ?? "",
                AssignedAt = a.AssignedAt,
                UnassignedAt = a.UnassignedAt,
                Reason = a.Reason,
                AssignedBy = a.AssignedByName
            }).ToList();

            return dto;
        }

        private static ApiResponse<T> Ok<T>(T data, string message) => new(ResponseType.Success, message, data);
        private static ApiResponse<T> Warn<T>(string message) => new(ResponseType.Warning, message, default!);
        private static ApiResponse<T> Fail<T>(string message) => new(ResponseType.Error, message, default!);

        private static CaseDto ToDto(CareCase c) => new()
        {
            Id = c.PublicId,
            ReferenceCode = c.ReferenceCode,
            PersonId = c.PersonPublicId,
            PersonName = c.PersonName,
            PersonPhone = c.PersonPhone,
            PersonDoNotContact = c.PersonDoNotContact,
            CampusName = c.CampusName,
            VolunteerId = c.AssignedVolunteerPublicId,
            VolunteerName = c.AssignedVolunteerName,
            TeamName = c.TeamName,
            Stage = c.Stage,
            Status = c.Status,
            Priority = c.Priority,
            CurrentStepNumber = c.CurrentStepNumber,
            NextStepDueOn = c.NextStepDueOn,
            NextActionOn = c.NextActionOn,
            AwaitingReviewSince = c.AwaitingReviewSince,
            OpenedAt = c.OpenedAt,
            FirstContactAt = c.FirstContactAt,
            LastContactAt = c.LastContactAt,
            ClosedAt = c.ClosedAt,
            CloseReason = c.CloseReason,
            ContactAttemptCount = c.ContactAttemptCount,
            RowVersion = c.RowVersion
        };

        private static CaseSummaryDto ToSummary(CareCase c) => new()
        {
            Id = c.PublicId,
            ReferenceCode = c.ReferenceCode,
            PersonName = c.PersonName,
            PersonPhone = c.PersonPhone,
            VolunteerName = c.AssignedVolunteerName,
            Stage = c.Stage,
            Status = c.Status,
            Priority = c.Priority,
            NextActionOn = c.NextActionOn,
            LastContactAt = c.LastContactAt,
            ContactAttemptCount = c.ContactAttemptCount,
            OpenedAt = c.OpenedAt
        };

        private static InteractionDto ToDto(CareInteraction i, DateTime today, CareCase? c) => new()
        {
            Id = i.PublicId,
            CaseId = c?.PublicId ?? i.CareCasePublicId,
            CaseReference = c?.ReferenceCode,
            PersonName = c?.PersonName,
            PersonPhone = c?.PersonPhone,
            Stage = i.Stage,
            SequenceNumber = i.SequenceNumber,
            Method = i.MethodCode,
            ScheduledOn = i.ScheduledOn,
            OccurredAt = i.OccurredAt,
            Status = i.Status,
            MadeContact = i.MadeContact,
            Outcome = i.OutcomeCode,
            OutcomeLabel = i.OutcomeLabel,
            Intent = i.IntentCode,
            IntentLabel = i.IntentLabel,
            DurationMinutes = i.DurationMinutes,
            Notes = i.Notes,
            VolunteerName = i.VolunteerName,
            IsOverdue = i.IsOverdue(today),
            RowVersion = i.RowVersion
        };

        private static EscalationDto ToDto(Escalation e, DateTime now) => new()
        {
            Id = e.PublicId,
            ReferenceCode = e.ReferenceCode,
            CaseId = e.CareCasePublicId,
            PersonName = e.PersonName,
            Reason = e.ReasonCode,
            ReasonLabel = e.ReasonLabel,
            RequiresProtocol = e.ReasonRequiresProtocol,
            Tier = e.Tier,
            Status = e.Status,
            Description = e.Description,
            RaisedByName = e.RaisedByName,
            AssignedToName = e.AssignedToName,
            RaisedAt = e.RaisedAt,
            AcknowledgedAt = e.AcknowledgedAt,
            ResolvedAt = e.ResolvedAt,
            Outcome = e.OutcomeCode,
            ResolutionNotes = e.ResolutionNotes,
            HoursWaiting = Math.Round(e.HoursWaiting(now), 1),
            ReminderCount = e.ReminderCount,
            PastorAlerted = e.PastorAlertedAt.HasValue,
            RowVersion = e.RowVersion
        };

        private static NoteDto ToDto(CareNote n) => new()
        {
            Id = n.PublicId,
            NoteType = n.NoteTypeLabel ?? n.NoteTypeCode,
            Body = n.Body,
            Tags = n.Tags,
            IsPrivate = n.IsPrivate,
            VisibleToRole = n.VisibleToRoleCode,
            CreatedAt = n.CreatedAt,
            CreatedBy = n.CreatedByName
        };
    }
}
