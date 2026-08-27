/*
 * Volunteer assignments screen.
 *
 * The markup is untouched — volunteers are trained on this layout, so every element
 * id, label, status badge and button caption is exactly as it was. Only the data
 * layer changed, because all four endpoints this page used were removed with the old
 * schema:
 *
 *   /volunteers/{id}/assignments   -> GET  /api/contacts/mine   (stage INITIAL_FOLLOW_UP)
 *   /nurture/volunteer/{id}/due    -> GET  /api/contacts/mine   (stage NURTURE)
 *   /followups/log-followup        -> POST /api/contacts/{id}/log
 *   /nurture/step/log              -> POST /api/contacts/{id}/log
 *
 * Both grids now come from ONE call. In the new model a planned contact is a
 * care_interaction whichever stage it belongs to, so "My List" and "Nurture Steps
 * Due" are two views of the same list split on `stage`.
 *
 * SECURITY CHANGE (invisible to the volunteer): the work list is no longer selected
 * by the `?volunteerid=` query parameter. It comes from the signed-in token, so
 * editing the URL can no longer show another volunteer's people. The parameter is
 * still read for the on-screen id, but it never decides what data is fetched.
 *
 * The four response options map onto the rules in care_progression_rule:
 *   Not contacted / No response -> NO_ANSWER      -> retry, or next nurture step
 *   Normal                      -> SPOKE + WANTS_CONNECTION -> start/continue nurture
 *   Needs follow-up             -> NEEDS_SUPPORT  -> escalate to the team lead
 *   Crisis                      -> CRISIS         -> escalate immediately
 * The wording on screen is unchanged; only what it means to the server is now data.
 */
var VId = "";

$(document).ready(function () {

    // Kept for the on-screen id only. Never used to choose whose work to load.
    var urlParams = new URLSearchParams(window.location.search);
    var volunteerIdParam = urlParams.get('volunteerid');

    $('#volunteerIdDisplay').text(volunteerIdParam || '-');

    var myWork = {};        // interaction id -> the interaction, for submit time

    /**
     * "Step 2/7" — the total comes from the case's own plan, not a constant. Plans are
     * per-campus and editable, so assuming 7 would quietly mislabel every step the
     * first time somebody creates a plan of a different length.
     *
     * Falls back to just "Step 2" when the plan size is unknown, which is honest
     * rather than guessing a denominator.
     */
    function stepBadge(i) {
        var total = i.nurtureTotalSteps;
        return total ? 'Step ' + i.sequenceNumber + '/' + total
                     : 'Step ' + i.sequenceNumber;
    }

    // ── helper: "10 Apr 2026" format ──────────────────────────────────────────
    function fmtDate(dateStr) {
        if (!dateStr) return '-';
        var d = new Date(dateStr);
        if (isNaN(d)) return '-';
        return d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
    }

    function esc(v) {
        return String(v == null ? '' : v)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    // ── PAGE SWITCHING ────────────────────────────────────────────────────────

    function showAssignmentsPage() {
        $('#followupPage').hide();
        $('#nurtureFollowupPage').hide();
        $('#assignmentsPage').show();
        window.scrollTo(0, 0);
    }

    function showFollowupPage(data) {
        $('#person_id').val(data.personId || '');
        $('#volunteer_id').val(VId || '');

        var initial = (data.name || 'V').trim().charAt(0).toUpperCase();
        $('#avatarInitial').text(initial);
        $('#displayPersonName').text(data.name || '');
        $('#displayPhone').text(data.phone || '');

        $('#followupPage .followup-topbar-title').text('Update Status');

        resetForm('', data.method);
        $('#modalResponse').html('');
        $('#submitFollowupBtn').prop('disabled', false).text('Submit');

        // Which planned contact this form will complete.
        $('#submitFollowupBtn').data('interactionid', data.interactionId);

        $('#assignmentsPage').hide();
        $('#followupPage').show();
        window.scrollTo(0, 0);
    }

    $('#topBackBtn, #bottomBackBtn').on('click', showAssignmentsPage);

    // ── Load the signed-in volunteer's work ───────────────────────────────────

    function loadMyWork() {
        $('#assignmentsGrid').html('');
        $('#responseContainer').html('');

        $.ajax({ url: API_BASE_URL + '/contacts/mine', method: 'GET' })
            .done(function (res) {
                var items = (res && res.data) || [];

                myWork = {};
                items.forEach(function (i) { myWork[i.id] = i; });

                renderAssignments(items.filter(function (i) { return i.stage !== 'NURTURE'; }));
                renderNurture(items.filter(function (i) { return i.stage === 'NURTURE'; }));
            })
            .fail(function (xhr) {
                $('#responseContainer').html(
                    '<div class="alert alert-danger">' +
                    (xhr.status === 401 ? 'Your session has expired. Please sign in again.'
                                        : 'Could not load your list. Please try again.') +
                    '</div>');
            });
    }

    function renderAssignments(items) {
        $('#listLabel').text('My List (' + items.length + ')');

        if (!items.length) {
            $('#responseContainer').html('<div class="alert alert-warning">No assignments found.</div>');
            $('#assignmentsGrid').html('');
            return;
        }

        var cards = items.map(function (i) {
            var name = i.personName || '';
            var phone = i.personPhone || '-';

            // A first attempt is PENDING; anything after it is a retry — the same two
            // states the screen has always shown.
            var isRetry = (i.sequenceNumber || 1) > 1;
            var status = isRetry ? 'RETRY PENDING' : 'PENDING';
            var statusClass = isRetry ? 'status-contacted' : 'status-pending';

            var actionButton =
                '<button class="action-btn ' + (isRetry ? 'btn-update update-status' : 'btn-start start-followup') + '"' +
                ' data-interactionid="' + esc(i.id) + '"' +
                ' data-personid="' + esc(i.caseId || '') + '"' +
                ' data-name="' + esc(name) + '"' +
                ' data-phone="' + esc(phone) + '">' +
                (isRetry ? 'Update Status' : 'Start Follow-up') +
                '</button>';

            var retryText = isRetry
                ? '<div class="retry-text">Attempt ' + i.sequenceNumber + '</div>'
                : '';

            return '' +
            '<div class="assignment-card">' +
                '<div class="card-header">' +
                    '<div>' +
                        '<div class="person-name">' + esc(name) + '</div>' +
                        '<div class="phone">' + esc(phone) + '</div>' +
                    '</div>' +
                    '<span class="status-badge ' + statusClass + '">' + status + '</span>' +
                '</div>' +
                retryText +
                '<div class="card-footer">' +
                    '<span class="assign-date">Due date: ' + fmtDate(i.scheduledOn) + '</span>' +
                    actionButton +
                '</div>' +
            '</div>';
        }).join('');

        $('#assignmentsGrid').html(cards);
    }

    function renderNurture(items) {
        var grid = document.getElementById('nurtureGrid');
        var title = document.getElementById('nurtureSectionTitle');

        grid.innerHTML = '';

        if (!items.length) { title.style.display = 'none'; return; }

        title.style.display = 'block';

        grid.innerHTML = items.map(function (i) {
            var isVisit = (i.method || '').toUpperCase() === 'VISIT';
            var methodLabel = isVisit ? 'Visit' : 'Call';
            var methodEmoji = isVisit ? '🏠' : '📞';

            return '' +
            '<div class="nurture-card">' +
                '<div class="card-header">' +
                    '<div>' +
                        '<div class="person-name">' + esc(i.personName) + '</div>' +
                        '<div class="phone">' + esc(i.personPhone || '') + '</div>' +
                    '</div>' +
                    '<span class="nurture-badge">' + stepBadge(i) + '</span>' +
                '</div>' +
                '<div style="margin-top:8px;display:flex;align-items:center;gap:8px">' +
                    '<span class="nurture-method-badge ' + (isVisit ? 'visit' : '') + '">' +
                        methodEmoji + ' ' + methodLabel + '</span>' +
                    (i.isOverdue ? '<span class="overdue-tag">Overdue</span>' : '') +
                    '<span style="font-size:11px;color:#9ca3af">Due: ' + fmtDate(i.scheduledOn) + '</span>' +
                '</div>' +
                '<div class="card-footer">' +
                    '<span class="assign-date"></span>' +
                    '<button class="action-btn btn-start nurture-log"' +
                        ' data-interactionid="' + esc(i.id) + '">Log Step</button>' +
                '</div>' +
            '</div>';
        }).join('');
    }

    // ── Header ────────────────────────────────────────────────────────────────

    function loadHeader() {
        $.ajax({ url: API_BASE_URL + '/auth/me', method: 'GET' })
            .done(function (res) {
                var me = (res && res.data) || {};

                $('#spnVolunteerName').text(me.fullName || 'N/A');
                VId = me.volunteerId || volunteerIdParam || '';

                if (VId) localStorage.setItem('volunteer_id', VId);

                loadTeamLead(me.teamId);
            })
            .fail(function () {
                $('#spnVolunteerName').text('Error');
                $('#spnteamLead').text('Error');
            });
    }

    function loadTeamLead(teamId) {
        if (!teamId) { $('#spnteamLead').text('N/A'); return; }

        $.ajax({ url: API_BASE_URL + '/teams', method: 'GET' })
            .done(function (res) {
                var team = ((res && res.data) || []).filter(function (t) { return t.id === teamId; })[0];
                $('#spnteamLead').text((team && team.leadName) || 'N/A');
            })
            .fail(function () { $('#spnteamLead').text('N/A'); });
    }

    loadHeader();
    loadMyWork();

    // The forms cannot be filled in until the vocabulary arrives, so build them first.
    loadReference();

    // "Did you contact them?" preselects the matching outcome rather than being a
    // separate answer — the outcome is what the server actually records.
    $(document).on('change', '#cs_yes, #cs_no', function () {
        var wantContact = this.id === 'cs_yes';

        var match = $('input[name="outcome"]').filter(function () {
            return truthy($(this).data('contactmade')) === wantContact;
        }).first();

        match.prop('checked', true).trigger('change');
    });

    // ── Open the follow-up form ───────────────────────────────────────────────

    $(document).on('click', '.action-btn', function () {
        var interactionId = $(this).data('interactionid');
        if (!interactionId) return;

        if ($(this).hasClass('nurture-log')) return openNurtureStep(interactionId);

        showFollowupPage({
            interactionId: interactionId,
            personId: $(this).data('personid'),
            name: $(this).data('name'),
            phone: $(this).data('phone')
        });
    });

    // -- Reference-driven form -------------------------------------------------
    //
    // Outcomes, intents, methods and escalation reasons all come from the server.
    // The MVP form offered four fixed choices and captured no intent at all, so the
    // rules that key on intent -- not interested, already at another church, asked
    // not to be contacted -- were unreachable and those cases could never close.

    var reference = { outcomes: [], intents: [], methods: [], escalationReasons: [] };

    function loadReference() {
        return $.ajax({ url: API_BASE_URL + '/contacts/reference', method: 'GET' })
            .then(function (res) {
                var d = (res && res.data) || {};

                reference.outcomes = d.outcomes || [];
                reference.intents = d.intents || [];
                reference.methods = d.methods || [];
                reference.escalationReasons = d.escalationReasons || [];

                buildForm('');    // initial follow-up
                buildForm('n');   // nurture step
            });
    }

    /** Element id for a form, so one set of code drives both. */
    function fid(prefix, base) {
        return prefix ? prefix + base.charAt(0).toUpperCase() + base.slice(1) : base;
    }

    function truthy(v) { return v === 1 || v === '1' || v === true; }

    function buildForm(prefix) {
        var radioName = prefix ? 'n_outcome' : 'outcome';
        var intentName = prefix ? 'n_intent' : 'intent';

        $('#' + fid(prefix, 'outcomeOptions')).html(reference.outcomes.map(function (o) {
            var id = radioName + '_' + o.code;
            return '<div class="radio-row">' +
                     '<input type="radio" name="' + radioName + '" id="' + id + '"' +
                       ' value="' + esc(o.code) + '"' +
                       ' data-contactmade="' + (o.contactMade ? '1' : '0') + '"' +
                       ' data-escalates="' + (o.opensEscalation ? '1' : '0') + '">' +
                     '<label for="' + id + '"' + (o.opensEscalation ? ' class="crisis"' : '') + '>' +
                       esc(o.label) + '</label>' +
                   '</div>';
        }).join(''));

        $('#' + fid(prefix, 'intentOptions')).html(reference.intents.map(function (i) {
            var id = intentName + '_' + i.code;
            return '<div class="radio-row">' +
                     '<input type="radio" name="' + intentName + '" id="' + id + '" value="' + esc(i.code) + '">' +
                     '<label for="' + id + '">' + esc(i.label) + '</label>' +
                   '</div>';
        }).join(''));

        var methodSelect = prefix ? '#n_method_code' : '#method_code';
        $(methodSelect).html(reference.methods.map(function (m) {
            return '<option value="' + esc(m.code) + '">' + esc(m.label) + '</option>';
        }).join(''));

        var reasonSelect = prefix ? '#n_escalation_reason' : '#escalation_reason';
        $(reasonSelect).html('<option value="">Choose a reason...</option>' +
            reference.escalationReasons.map(function (r) {
                return '<option value="' + esc(r.code) + '"' +
                       ' data-protocol="' + (r.requiresProtocol ? '1' : '0') + '">' +
                       esc(r.label) + '</option>';
            }).join(''));

        $('input[name="' + radioName + '"]').on('change', function () { syncForm(prefix); });
        $(reasonSelect).on('change', function () { syncProtocolNote(prefix); });
    }

    /**
     * Shows only the questions the chosen outcome actually needs, using the flags
     * the server sent rather than a hardcoded list of codes.
     */
    function syncForm(prefix) {
        var radioName = prefix ? 'n_outcome' : 'outcome';
        var chosen = $('input[name="' + radioName + '"]:checked');

        var contactMade = truthy(chosen.data('contactmade'));
        var escalates = truthy(chosen.data('escalates'));

        $('#' + fid(prefix, 'intentSection')).toggle(chosen.length > 0 && contactMade);
        $('#' + fid(prefix, 'escalationSection')).toggle(chosen.length > 0 && escalates);

        // Duration only means something for a conversation or a visit.
        $('#' + fid(prefix, 'durationSection')).toggle(contactMade);

        syncProtocolNote(prefix);
    }

    function syncProtocolNote(prefix) {
        var reasonSelect = prefix ? '#n_escalation_reason' : '#escalation_reason';

        $('#' + fid(prefix, 'protocolNote'))
            .toggle(truthy($(reasonSelect + ' option:selected').data('protocol')));
    }

    /** Local date-time for the datetime-local input, which carries no timezone. */
    function nowLocal() {
        var d = new Date();
        d.setMinutes(d.getMinutes() - d.getTimezoneOffset());
        return d.toISOString().slice(0, 16);
    }

    /**
     * Clears a form ready for the next contact. `suggestedMethod` pre-selects how the
     * step was planned to happen — a nurture visit opens with Visit chosen — while
     * still letting the volunteer say it actually went differently.
     */
    function resetForm(prefix, suggestedMethod) {
        var radioName = prefix ? 'n_outcome' : 'outcome';
        var intentName = prefix ? 'n_intent' : 'intent';

        $('input[name="' + radioName + '"]').prop('checked', false);
        $('input[name="' + intentName + '"]').prop('checked', false);

        $(prefix ? '#n_escalation_reason' : '#escalation_reason').val('');
        $(prefix ? '#n_escalation_description' : '#escalation_description').val('');
        $(prefix ? '#n_duration_min' : '#call_duration_min').val('');
        $(prefix ? '#nurture_notes' : '#notes').val('');
        $(prefix ? '#n_occurred_at' : '#occurred_at').val(nowLocal());

        if (suggestedMethod) {
            $(prefix ? '#n_method_code' : '#method_code').val(String(suggestedMethod).toUpperCase());
        }

        // Both grouped questions start unanswered, so nothing is recorded by default.
        $(prefix ? '#nc_yes, #nc_no' : '#cs_yes, #cs_no').prop('checked', false);

        syncForm(prefix);
    }

    /**
     * Reads one form into a LogInteractionRequest. Returns a string instead when
     * something is missing, so the caller can show it.
     */
    function readForm(prefix) {
        var radioName = prefix ? 'n_outcome' : 'outcome';
        var intentName = prefix ? 'n_intent' : 'intent';
        var chosen = $('input[name="' + radioName + '"]:checked');

        if (!chosen.length) return 'Please choose what happened.';

        var body = {
            outcomeCode: chosen.val(),
            methodCode: $(prefix ? '#n_method_code' : '#method_code').val() || null,
            notes: $(prefix ? '#nurture_notes' : '#notes').val() || null
        };

        var intent = $('input[name="' + intentName + '"]:checked').val();
        if (intent) body.intentCode = intent;

        var occurred = $(prefix ? '#n_occurred_at' : '#occurred_at').val();
        if (occurred) body.occurredAt = new Date(occurred).toISOString();

        var duration = parseInt($(prefix ? '#n_duration_min' : '#call_duration_min').val() || '0', 10);
        if (duration > 0) body.durationMinutes = duration;

        if (truthy(chosen.data('escalates'))) {
            var reason = $(prefix ? '#n_escalation_reason' : '#escalation_reason').val();
            var description = $(prefix ? '#n_escalation_description' : '#escalation_description').val();

            if (!reason) return 'Please choose why this needs a team lead.';

            // The server requires at least 10 characters, so say so here rather than
            // letting the volunteer find out after pressing submit.
            if (!description || description.trim().length < 10)
                return 'Please describe the concern so the team lead can act on it.';

            body.escalationReasonCode = reason;
            body.escalationDescription = description.trim();
        }

        return body;
    }

    /** Posts a completed contact and reports what the rules decided. */
    function logInteraction(interactionId, body, $btn, $message, onDone) {
        var interaction = myWork[interactionId];

        if (!interaction) {
            $message.html('<div class="alert alert-danger mt-2">That item is no longer in your list. Please refresh.</div>');
            return;
        }

        body.rowVersion = interaction.rowVersion;

        $.ajax({
            url: API_BASE_URL + '/contacts/' + encodeURIComponent(interactionId) + '/log',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(body)
        })
            .done(function (res) {
                // responseType: 0 Success, 1 Warning, 2 Error.
                if (!res || res.responseType !== 0) {
                    $message.html('<div class="alert alert-danger mt-2">' +
                        esc((res && res.message) || 'Could not save.') + '</div>');
                    onDone(false);
                    return;
                }

                // The engine explains what happens next — "Step 3 is due 22 Aug",
                // "Sent to the team lead". Worth showing rather than a bare success.
                var explanation = (res.data && res.data.explanation) || 'Saved.';

                $message.html('<div class="alert alert-success mt-2">' + esc(explanation) + '</div>');

                loadMyWork();
                setTimeout(showAssignmentsPage, 1400);
            })
            .fail(function (xhr) {
                $message.html('<div class="alert alert-danger mt-2">' +
                    (xhr.status === 401 ? 'Your session has expired. Please sign in again.'
                                        : 'Could not save. Please try again.') + '</div>');
                onDone(false);
            });
    }

    // ── Submit follow-up ──────────────────────────────────────────────────────

    $('#submitFollowupBtn').on('click', function () {
        var $btn = $(this);
        var body = readForm('');

        // readForm returns the reason as a string when something is missing.
        if (typeof body === 'string') {
            $('#modalResponse').html('<div class="alert alert-warning mt-2">' + esc(body) + '</div>');
            return;
        }

        $btn.prop('disabled', true).text('Saving…');
        $('#modalResponse').html('');

        logInteraction($btn.data('interactionid'), body, $btn, $('#modalResponse'), function () {
            $btn.prop('disabled', false).text('Submit');
        });
    });

    // ── Logout ────────────────────────────────────────────────────────────────

    $(document).on('click', '.logout-btn, #logoutBtn', function () {
        if (window.RmAuth && RmAuth.logout) { RmAuth.logout(); return; }
        window.location.href = 'Login.html';
    });

    // ══════════════════════════════════════════
    // NURTURE STEPS
    // ══════════════════════════════════════════

    function openNurtureStep(interactionId) {
        var i = myWork[interactionId];
        if (!i) return;

        var isVisit = (i.method || '').toUpperCase() === 'VISIT';

        document.getElementById('nurture_step_id').value = interactionId;
        document.getElementById('nurture_sequence_id').value = i.caseId || '';
        document.getElementById('nurture_person_id').value = i.caseId || '';
        document.getElementById('nurture_volunteer_id').value = VId || '';
        document.getElementById('nurturePersonName').textContent = i.personName || '';
        document.getElementById('nurturePersonPhone').textContent = i.personPhone || '';
        document.getElementById('nurture_step_method').value = isVisit ? 'House Visit' : 'Phone Call';
        document.getElementById('nurtureAvatarInitial').textContent =
            (i.personName || 'V').trim().charAt(0).toUpperCase();
        document.getElementById('nurtureStepBadge').textContent = stepBadge(i);

        var mb = document.getElementById('nurtureMethodBadge');
        mb.textContent = isVisit ? '🏠 Visit' : '📞 Call';
        mb.className = 'nurture-method-badge' + (isVisit ? ' visit' : '');

        // Opens with the method the plan intended, which the volunteer can change.
        resetForm('n', isVisit ? 'VISIT' : 'CALL');
        document.getElementById('nurtureModalResponse').innerHTML = '';
        document.getElementById('nurtureResponseSection').style.display = 'block';

        var submit = document.getElementById('submitNurtureStepBtn');
        submit.disabled = false;
        submit.textContent = 'Submit Step';

        document.getElementById('assignmentsPage').style.display = 'none';
        document.getElementById('followupPage').style.display = 'none';
        document.getElementById('nurtureFollowupPage').style.display = 'block';
        window.scrollTo(0, 0);
    }

    // Exposed because the markup is unchanged and may still reference it inline.
    window.openNurtureStep = openNurtureStep;

    function closeNurturePage() {
        document.getElementById('nurtureFollowupPage').style.display = 'none';
        document.getElementById('followupPage').style.display = 'none';
        document.getElementById('assignmentsPage').style.display = 'block';
    }

    document.getElementById('nurtureBackBtn').addEventListener('click', closeNurturePage);
    document.getElementById('nurtureBottomBackBtn').addEventListener('click', closeNurturePage);

    // "Did you complete this step?" is now a shortcut rather than a separate answer:
    // saying no simply preselects the not-reached outcome, which the volunteer can
    // refine. The outcome itself is what the server records.
    document.getElementById('nc_no').addEventListener('change', function () {
        if (!this.checked) return;

        var notReached = $('input[name="n_outcome"]').filter(function () {
            return !truthy($(this).data('contactmade'));
        }).first();

        notReached.prop('checked', true).trigger('change');
    });

    document.getElementById('nc_yes').addEventListener('change', function () {
        if (!this.checked) return;

        var reached = $('input[name="n_outcome"]').filter(function () {
            return truthy($(this).data('contactmade'));
        }).first();

        reached.prop('checked', true).trigger('change');
    });

    document.getElementById('submitNurtureStepBtn').addEventListener('click', function () {
        var btn = this;
        var interactionId = document.getElementById('nurture_step_id').value;
        var $message = $('#nurtureModalResponse');
        var body = readForm('n');

        if (typeof body === 'string') {
            $message.html('<div class="alert alert-warning mt-2">' + esc(body) + '</div>');
            return;
        }

        btn.disabled = true;
        btn.textContent = 'Submitting...';
        $message.html('');

        logInteraction(interactionId, body, $(btn), $message, function () {
            btn.disabled = false;
            btn.textContent = 'Submit Step';
        });
    });
});
