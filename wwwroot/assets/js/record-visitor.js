/*
 * Visitor intake — the data-entry operator's screen.
 *
 * Deliberately narrow. A DATA_ENTRY account can record a visitor, check for
 * duplicates and CORRECT a record it got wrong; it still cannot list people or
 * browse cases (GET /api/people is VolunteerOrAbove). So this is a capture form,
 * not a CRUD console, and the "recorded just now" panel is built from what this
 * session created rather than by reading the list back.
 *
 * Flow:
 *   POST   /api/people              -> create the person
 *   POST   /api/cases               -> open a case so a volunteer follows up (optional)
 *   GET    /api/people/lookup       -> warn about an existing record before saving,
 *                                      and find one to correct
 *   GET    /api/people/{id}/intake  -> the record to correct, intake fields only
 *   PUT    /api/people/{id}         -> save the correction
 *   GET    /api/areas/options       -> the area type-ahead (via AreaPicker)
 *
 * CORRECTING: operators are the ones who mishear a name or transpose a digit, so
 * they are the ones who should be able to fix it. What they can see and change is
 * exactly what this form collects — the /intake endpoint exists so they do not need
 * the full person record, which carries lifecycle, do-not-contact and pastoral
 * notes. Those stay on other screens for other roles.
 *
 * The two modes are not the same request. Intake POSTs a `contacts` array; a
 * correction PUTs `mobile` and `email` as plain fields, and sends nothing about the
 * VISIT (how they found us, the date, the follow-up) because those live on the case.
 * Anything this form shows in correction mode is something an update can store.
 *
 * Requires auth.js, toast.js, admin-shell.js and area-picker.js.
 */
(function () {
    'use strict';

    var ROLES = ['ADMIN', 'PASTOR', 'TEAM_LEAD', 'VOLUNTEER', 'DATA_ENTRY'];

    var form, saveBtn, clearBtn, dupNotice, formError, statusHint, sessionList;
    var areaPicker;
    var recorded = [];

    // Set when the API refuses a save because someone shares the contact number.
    // The next submit carries allowDuplicate, so recording a duplicate is always a
    // second, deliberate action rather than something that happens by accident.
    var duplicateAcknowledged = false;

    // The record being corrected: { id, rowVersion, name }, or null when recording
    // somebody new. Everything that behaves differently in the two modes reads this.
    var editing = null;

    // Who already holds the mobile number typed in, from /api/people/base-visitor:
    // { baseVisitorId, baseVisitorName, householdNames }. Null when nobody does, which
    // makes the person being recorded the base visitor for that number.
    var baseVisitor = null;

    // The administrator's assignment.auto_assign_on_intake rule, from /api/intake-rules.
    // Optimistic until the answer arrives: the server enforces it either way, so the
    // worst an unlucky race does is show a box that turns out to be moot, and assuming
    // the opposite would hide the box for everybody when the request is slow.
    var autoAssignOnIntake = true;

    document.addEventListener('DOMContentLoaded', function () {
        AdminShell
            .boot({
                roles: ROLES,
                active: { href: '/pages/intake/record-visitor.html', area: 'Intake' }
            })
            .then(function (ok) {
                if (!ok) return;    // boot already redirected to the login page
                document.getElementById('pageBody').hidden = false;
                init();
            });
    });

    function init() {
        form        = document.getElementById('intakeForm');
        saveBtn     = document.getElementById('saveBtn');
        clearBtn    = document.getElementById('clearBtn');
        dupNotice   = document.getElementById('dupNotice');
        formError   = document.getElementById('formError');
        statusHint  = document.getElementById('statusHint');
        sessionList = document.getElementById('sessionList');

        loadReference();
        loadIntakeRules();
        loadCampuses();
        setToday();
        wireFollowUpToggle();
        wireOpenCaseButton();
        wireResidence();

        form.addEventListener('submit', onSubmit);
        clearBtn.addEventListener('click', function () { stopEditing(); resetForm(true); });

        document.getElementById('correctBtn')
            .addEventListener('click', openFinder);

        document.getElementById('findClose')
            .addEventListener('click', closeFinder);

        document.getElementById('cancelEditBtn')
            .addEventListener('click', function () { stopEditing(); resetForm(true); });

        // Debounced: the server floor is three characters and a request per keystroke
        // would ask the same question four times on the way to a name.
        var findTimer = null;

        document.getElementById('findTerm').addEventListener('input', function () {
            window.clearTimeout(findTimer);
            findTimer = window.setTimeout(searchPeople, 250);
        });

        // Delegated, because the result rows are rewritten on every search.
        document.getElementById('findResults').addEventListener('click', function (event) {
            var id = event.target && event.target.getAttribute
                ? event.target.getAttribute('data-correct')
                : null;

            if (id) loadForCorrection(id);
        });

        // Any edit invalidates a duplicate warning that was about the previous values.
        form.addEventListener('input', function (e) {
            clearFieldError(e.target);

            if (duplicateAcknowledged || !dupNotice.hidden) {
                duplicateAcknowledged = false;
                hide(dupNotice);
                saveBtn.textContent = defaultSaveLabel();
            }
        });

        // Digits-only, ten of them, starting 6-9 — enforced as they type.
        MobileInput.attach(document.getElementById('mobile'), {
            hint: document.getElementById('mobileHint'),
            hintText: '10 digits, starting 6-9. Checked against existing records ' +
                      'as you leave the field.'
        });

        document.getElementById('mobile').addEventListener('blur', checkForDuplicate);

        // The prompt names the visitor, and the number is often typed before the
        // name. Without this the question stays "what is this visitor's relationship
        // with John?" after the operator has already written "Mary" above it.
        document.getElementById('givenName').addEventListener('input', function () {
            if (baseVisitor) showRelationship(baseVisitor);
        });

        // Answering it is what clears the complaint about not having answered.
        document.getElementById('relationshipCode').addEventListener('change', function () {
            clearFieldError(this);
        });
    }

    // ---------------------------------------------------------------- reference

    /**
     * Age bands are CHECK-constrained in the database, so the options come from the
     * server rather than being hard-coded here — a mismatch would be rejected on save.
     */
    function loadReference() {
        fetch(API_BASE_URL + '/people-reference')
            .then(function (res) { return res.json(); })
            .then(function (body) {
                var data = (body && body.data) || {};
                var bands = data.ageBands || [];
                var select = document.getElementById('ageBand');

                bands.forEach(function (code) {
                    var option = document.createElement('option');
                    option.value = code;
                    option.textContent = labelForBand(code);
                    select.appendChild(option);
                });

                // Wife, Son, Brother... from relationship_type, for the same reason
                // the age bands come from the server: the column is foreign-keyed to
                // that table, so a code this screen invented would be refused on save.
                var relationships = document.getElementById('relationshipCode');

                (data.relationshipTypes || []).forEach(function (r) {
                    var option = document.createElement('option');
                    option.value = r.code;
                    option.textContent = r.label;
                    relationships.appendChild(option);
                });
            })
            .catch(function () {
                showToast('Could not load the age-group list.', 'warning');
            });
    }

    /**
     * The administrator rules this screen has to obey.
     *
     * Only one so far: whether a case opened here is assigned to a volunteer there
     * and then, or left for the assignment job. Fetching it is what stops the form
     * offering a choice the server will not honour — the complaint being that the
     * box said "assigns it to an available volunteer" and did exactly that, with the
     * setting turned off.
     *
     * A failure is not fatal. The box stays as it was and the server still applies
     * the rule; the operator just sees the outcome in the save message rather than
     * before they save.
     */
    function loadIntakeRules() {
        fetch(API_BASE_URL + '/intake-rules')
            .then(function (res) { return res.json(); })
            .then(function (body) {
                var rules = (body && body.data) || {};

                if (typeof rules.autoAssignOnIntake === 'boolean') {
                    autoAssignOnIntake = rules.autoAssignOnIntake;
                }

                syncFollowUp();
            })
            .catch(function () { /* keep the default; the server decides anyway */ });
    }

    function labelForBand(code) {
        if (code === 'UNDER_18') return 'Under 18';
        if (code === 'OVER_60')  return 'Over 60';
        return String(code).replace('_', '–');   // 26_35 -> 26–35
    }

    function setToday() {
        var input = document.getElementById('firstVisitOn');
        var now = new Date();
        var month = String(now.getMonth() + 1).padStart(2, '0');
        var day = String(now.getDate()).padStart(2, '0');

        input.value = now.getFullYear() + '-' + month + '-' + day;
    }

    /**
     * The campus this visitor is being recorded at.
     *
     * It matters more than it looks: the case is opened at the PERSON's campus, and
     * auto-assignment only considers volunteers at that campus. Filing someone
     * against the wrong site means nobody who serves them can pick them up.
     *
     * The server returns only campuses this operator may file against — one for a
     * campus-scoped account, all of them for an organisation-wide one. With a single
     * option the field stays hidden and the server applies the same default.
     */
    function loadCampuses() {
        fetch(API_BASE_URL + '/campuses/options')
            .then(function (res) { return res.json(); })
            .then(function (body) {
                var list = (body && body.data) || [];
                var select = document.getElementById('campus');

                select.innerHTML = list.map(function (c) {
                    return '<option value="' + c.id + '">' + escapeHtml(c.name) + '</option>';
                }).join('');

                // One choice is not a choice. Leave it hidden and let the server
                // default to the operator's own campus.
                document.getElementById('campusField').hidden = list.length < 2;

                // Areas belong to a campus, so a chosen area stops being valid the
                // moment the campus changes — the server refuses one from elsewhere.
                // Clearing it makes that visible now rather than at save time.
                document.getElementById('campus').addEventListener('change', function () {
                    if (areaPicker) areaPicker.clear();
                });
            })
            .catch(function () {
                // Non-fatal: without the picker the server still files the visitor at
                // the operator's own campus, which is right in the single-campus case
                // and the only sensible fallback in any other.
                document.getElementById('campusField').hidden = true;
            });
    }

    function escapeHtml(value) {
        return String(value == null ? '' : value)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    /**
     * "Lives locally" decides which half of the address section is on screen.
     *
     * Checked — the normal case — the only question is which area, and it is
     * required: a local visitor is followed up in person, and the area is what
     * decides which volunteer can take them. Unchecked, the screen falls back to
     * exactly the fields it has always had, because an out-of-town address is a
     * one-off that no volunteer will be matched against.
     *
     * The hidden half is not merely hidden. Its values are cleared and the picker
     * is reset, so a full address typed before the box was unticked cannot be
     * submitted invisibly.
     */
    function wireResidence() {
        var toggle = document.getElementById('isLocal');
        var localBlock = document.getElementById('localBlock');
        var awayBlock = document.getElementById('awayBlock');

        areaPicker = AreaPicker.attach({
            input: 'areaName',
            results: 'areaResults',

            // Read fresh each time: changing the campus changes which areas exist,
            // and an area from another campus is one the server refuses.
            campusId: function () {
                return document.getElementById('campusField').hidden
                    ? ''
                    : document.getElementById('campus').value;
            },

            onChange: function () { clearFieldError(document.getElementById('areaName')); }
        });

        function sync() {
            var local = toggle.checked;

            localBlock.hidden = !local;
            awayBlock.hidden = local;

            if (local) {
                document.getElementById('addressLine').value = '';
                document.getElementById('locality').value = '';
                document.getElementById('postalCode').value = '';
            } else if (areaPicker) {
                areaPicker.clear();
            }

            clearFieldError(document.getElementById('areaName'));

            // Where they live decides whether a volunteer can be given them at all.
            syncFollowUp();
        }

        toggle.addEventListener('change', sync);
        sync();
    }

    function wireFollowUpToggle() {
        document.getElementById('startFollowUp')
            .addEventListener('change', syncFollowUp);

        document.getElementById('assignNow')
            .addEventListener('change', syncFollowUp);

        syncFollowUp();
    }

    /**
     * Whether an immediate assignment is on the table at all.
     *
     * Two things can take it off, and neither is this screen's opinion:
     *
     *   • the administrator has set "Auto assign on intake" to No, which means the
     *     assignment job places cases rather than the intake desk
     *   • the visitor does not live locally, so there is no area to match a
     *     volunteer against — volunteers go and see people
     *
     * Either way the case is still OPENED. It waits unassigned, on the same queue a
     * team lead already works from. What changes is only that nobody is handed the
     * visitor here and now.
     */
    function autoAssignAllowed() {
        return autoAssignOnIntake && document.getElementById('isLocal').checked;
    }

    /**
     * Whether to actually assign this visitor here and now.
     *
     * Two things have to be true: an immediate assignment must be POSSIBLE, and the
     * operator must not have chosen to leave it for the job. Keeping them apart is
     * the whole point of the change — the old single box made "do not assign now"
     * and "do not follow up at all" the same click, and only the second one happened.
     *
     * The server checks the possible half again regardless. This only keeps the
     * request honest about what the operator was shown.
     */
    function assignNowChosen() {
        return autoAssignAllowed() && document.getElementById('assignNow').checked;
    }

    /**
     * Shows or hides the follow-up box to match. Hidden means "not a choice you have",
     * so the box is not merely disabled — a disabled tick that stays ticked reads as
     * a promise being kept.
     *
     * The note in its place says which rule applied. Without it an operator who is
     * used to seeing the box would assume the screen was broken.
     */
    function syncFollowUp() {
        var toggle = document.getElementById('startFollowUp');
        var assign = document.getElementById('assignNow');
        var assignField = document.getElementById('assignNowField');
        var note = document.getElementById('followUpNote');
        var priorityField = document.getElementById('priorityField');

        // Correcting a record starts no follow-up at all; startEditing owns that and
        // must not be argued with here.
        if (editing) {
            note.hidden = true;
            return;
        }

        // No case means there is nothing to assign and no priority to set. The
        // "open a case" box itself always stays on screen: it is the one question
        // whose answer is always the operator's.
        var opening = toggle.checked;
        var possible = autoAssignAllowed();

        assignField.hidden = !opening || !possible;
        priorityField.hidden = !opening;

        if (!opening) {
            // Said out loud, because this is the click that used to look like
            // "assign later" and actually meant "never".
            note.textContent = 'No case will be opened, so nobody will follow up with ' +
                               'this visitor — not now and not later.';
            note.hidden = false;
            return;
        }

        if (!possible) {
            // Forced back on, so the request matches the note. An immediate assignment
            // is off the table, and a stale untick would otherwise travel with a
            // request whose answer was already decided.
            assign.checked = true;

            note.textContent = document.getElementById('isLocal').checked
                ? 'A case will be opened and queued. Assigning at intake is turned off in ' +
                  'settings, so the assignment job places it.'
                : 'A case will be opened and queued. Visitors from out of town are not ' +
                  'assigned to a volunteer automatically — a team lead places them.';

            note.hidden = false;
            return;
        }

        if (!assign.checked) {
            note.textContent = 'A case will be opened and left unassigned. The assignment ' +
                               'job places it with the least-loaded volunteer on its next run.';
            note.hidden = false;
            return;
        }

        note.hidden = true;
    }

    // ---------------------------------------------------------------- duplicates

    /**
     * Pre-emptive check as the operator leaves the mobile field. The server checks
     * again on save — this only saves them typing the rest of the form first.
     * Contact values come back masked, so this shows that a record exists without
     * disclosing anyone's number.
     */
    function checkForDuplicate() {
        var mobile = document.getElementById('mobile').value.trim();

        // Only worth asking once the number is complete: a partial number matches
        // half the directory and the warning would be noise.
        if (!MobileInput.isValid(mobile)) return;

        // Correcting a record asks nothing. The number belongs to the person on
        // screen, and a correction never creates anybody to be related to them.
        if (editing) { hideRelationship(); return; }

        fetch(API_BASE_URL + '/people/base-visitor?mobile=' + encodeURIComponent(mobile))
            .then(function (res) { return res.json(); })
            .then(function (body) {
                var info = (body && body.data) || {};

                // Late answers are dropped. The operator may have corrected the
                // number while this was in flight, and showing "John already holds
                // this number" beside a different number is worse than showing
                // nothing - the save re-checks against whatever is finally typed.
                if (document.getElementById('mobile').value.trim() !== mobile) return;

                if (info.relationshipRequired) showRelationship(info);
                else hideRelationship();
            })
            .catch(function () { /* the save-time check is the one that counts */ });
    }

    /**
     * Asks who this visitor is to the family already on the number.
     *
     * This REPLACES the old "this number is already on file / save anyway" refusal.
     * A family shares a phone, so the number is not the problem and there is nothing
     * to override - the only thing missing is which of them this person is.
     *
     * The base visitor named here is the FIRST person registered on the number and
     * never the most recent, so the household keeps one centre however many people
     * are added to it. The server decides that, not this screen; what is shown here
     * is only what it answered a moment ago.
     */
    function showRelationship(info) {
        baseVisitor = info;

        var field = document.getElementById('relationshipField');
        var notice = document.getElementById('householdNotice');
        var label = document.getElementById('relationshipLabel');

        var household = (info.householdNames || []).map(function (n) {
            return '<li>' + AdminShell.escapeHtml(n) + '</li>';
        }).join('');

        notice.innerHTML =
            '<strong>' + AdminShell.escapeHtml(info.baseVisitorName) +
            ' already uses this number.</strong>' +
            (household ? '<ul class="dup-list">' + household + '</ul>' : '<br>') +
            'That is fine - a family often shares one phone. Say how this visitor ' +
            'is related and they will be saved alongside them.';

        // Named, not "the existing visitor". The operator is about to ask the person
        // in front of them, and the question they can actually say out loud is
        // "what is Mary's relationship with John?".
        var who = value('givenName') || 'this visitor';

        label.innerHTML =
            'What is ' + AdminShell.escapeHtml(who) + '\u2019s relationship with ' +
            AdminShell.escapeHtml(info.baseVisitorName) + '? ' +
            '<span class="required-mark" aria-hidden="true">*</span>';

        field.hidden = false;
    }

    function hideRelationship() {
        baseVisitor = null;

        document.getElementById('relationshipField').hidden = true;
        document.getElementById('relationshipCode').value = '';
        clearFieldError(document.getElementById('relationshipCode'));
    }

    // ---------------------------------------------------------------- submit

    function onSubmit(event) {
        event.preventDefault();

        hide(formError);

        var payload = readForm();
        var problem = validate(payload);

        if (problem) {
            showError(problem.message);
            markField(problem.field);
            return;
        }

        setBusy(true, 'Saving…');

        // Correcting an existing record goes to PUT with the row version, so a save
        // is refused rather than silently overwriting somebody who edited first.
        // Recording somebody new goes to POST, where the duplicate override applies —
        // it is meaningless on an update, which is by definition the same person.
        var correcting = !!editing;
        var request = buildPersonRequest(payload, correcting);
        var url = API_BASE_URL + '/people';
        var method = 'POST';

        if (correcting) {
            request.rowVersion = editing.rowVersion;
            url += '/' + encodeURIComponent(editing.id);
            method = 'PUT';
        } else {
            request.allowDuplicate = duplicateAcknowledged;
        }

        var httpOk = true;

        fetch(url, {
            method: method,
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(request)
        })
            .then(function (res) { httpOk = res.ok; return res.json().catch(function () { return null; }); })
            .then(function (body) {
                // fetch does not reject on 4xx, and a ProblemDetails body carries no
                // responseType — so a validation failure used to fall THROUGH every
                // check below and continue as though the save had worked, with
                // `person` undefined and nothing shown. Anything but 2xx stops here.
                if (!httpOk) {
                    setBusy(false);
                    showError(describeProblem(body, 'This visitor could not be saved.'));
                    return;
                }

                // responseType: 0 Success, 1 Warning, 2 Error.
                if (!body || body.responseType === 2) {
                    setBusy(false);
                    showError((body && body.message) || 'Could not save this visitor.');
                    return;
                }

                if (body.responseType === 1) {
                    setBusy(false);

                    // ONLY a duplicate gets the override. This used to treat every
                    // warning as one, so an unknown age band or a campus the operator
                    // cannot file against came back offering "save anyway as a
                    // separate person" — an answer to a question nobody asked, and no
                    // sign of what was actually wrong.
                    if (body.code === 'relationship_required') {
                        // The number was claimed between the blur check and the save,
                        // or that check never ran. Not an error and not a duplicate:
                        // ask the question and let them save again.
                        askRelationshipAfterRefusal(payload.mobile, body.message);
                    } else if (body.code === 'duplicate_contact') {
                        offerDuplicateOverride(body.message);
                    } else {
                        showError(body.message || 'This visitor could not be saved.');
                    }

                    return;
                }

                var person = body.data;

                // A correction ends here. It must never open a follow-up case: the
                // point was to fix what was typed, and starting pastoral work off the
                // back of a spelling fix would put a volunteer in front of somebody
                // who is already being looked after.
                if (correcting) {
                    setBusy(false);
                    showToast('Record corrected.', 'success');
                    stopEditing();
                    resetForm();
                    return;
                }

                if (!payload.startFollowUp) {
                    finish(person, false);
                    return;
                }

                openCase(person, payload);
            })
            .catch(function () {
                setBusy(false);
                showError('Could not reach the server. Check your connection and try again.');
            });
    }

    /**
     * Opens the care case. The person is already saved at this point, so a failure
     * here is reported as a partial success — telling the operator "nothing saved"
     * would make them enter the visitor twice.
     */
    function openCase(person, payload) {
        var request = {
            personId: person.id,

            // What the screen believes. The server checks the same two rules itself
            // and will refuse regardless — this only keeps the request honest about
            // what the operator was actually shown.
            autoAssign: payload.autoAssign,

            priority: payload.priority || 'NORMAL'
        };

        if (payload.connectionSource) request.connectionSource = payload.connectionSource;
        if (payload.firstVisitOn)     request.firstVisitOn = payload.firstVisitOn;

        var caseHttpOk = true;

        fetch(API_BASE_URL + '/cases', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(request)
        })
            .then(function (res) { caseHttpOk = res.ok; return res.json().catch(function () { return null; }); })
            .then(function (body) {
                // The person IS saved by now, so every failure here is a partial
                // success — saying "could not save" would make the operator enter
                // them a second time. A warning counts too: "already has an open
                // case" is a refusal, not a success.
                if (!caseHttpOk || !body || body.responseType === 2 || body.responseType === 1) {
                    finish(person, false,
                        describeProblem(body, 'Follow-up could not be started.'));
                    return;
                }

                // A case with no volunteer on it is a real, expected outcome now —
                // assignment at intake may be off, or the visitor may be from out of
                // town. Say which of the two happened rather than reporting a
                // follow-up that nobody has picked up.
                var assignedTo = (body.data && body.data.volunteerName) || '';

                finish(person, true, null, assignedTo);
            })
            .catch(function () {
                finish(person, false, 'Saved, but follow-up could not be started.');
            });
    }

    /**
     * Pulls something readable out of any failure shape this API produces.
     *
     * Three reach here: an ApiResponse warning (message), a ProblemDetails from model
     * validation (title plus per-field `errors`), and nothing at all when the body was
     * not JSON. The per-field errors are the useful part — the title is always the
     * same "One or more validation errors occurred."
     */
    function describeProblem(body, fallback) {
        if (!body) return fallback;

        if (body.errors) {
            var messages = [];

            Object.keys(body.errors).forEach(function (field) {
                (body.errors[field] || []).forEach(function (m) {
                    if (messages.indexOf(m) === -1) messages.push(m);
                });
            });

            if (messages.length) return messages.join(' ');
        }

        return body.message || body.detail || fallback;
    }

    /**
     * @param {boolean} followUpStarted  a case was opened
     * @param {string=} warning          the case could NOT be opened
     * @param {string=} assignedTo       the volunteer it went to, blank when queued
     */
    function finish(person, followUpStarted, warning, assignedTo) {
        setBusy(false);

        var name = (person && person.fullName) || 'Visitor';

        if (warning) {
            showToast(warning, 'warning');
            showError(name + ' was saved, but follow-up did not start. ' +
                      'Tell a team lead so it is picked up manually.');
        } else if (!followUpStarted) {
            showToast(name + ' recorded.', 'success');
        } else if (assignedTo) {
            showToast(name + ' recorded and assigned to ' + assignedTo + '.', 'success');
        } else {
            // Not a warning: the case exists and is on the queue a team lead works
            // from. It just has nobody's name on it yet, and saying so here is the
            // difference between "handled" and "waiting".
            showToast(name + ' recorded. The case is queued for assignment.', 'success');
        }

        addToSession(person, followUpStarted, assignedTo);
        resetForm(false);
        document.getElementById('givenName').focus();
    }

    // ---------------------------------------------------------------- helpers

    function readForm() {
        var isLocal = document.getElementById('isLocal').checked;

        // Only the visible half is read. The other half was cleared when the box
        // was toggled, but reading it anyway would make that clearing the only
        // thing standing between a hidden value and the request.
        var area = (isLocal && areaPicker) ? areaPicker.value() : { id: null, name: '' };

        return {
            givenName:        value('givenName'),
            familyName:       value('familyName'),
            ageBand:          value('ageBand'),
            gender:           value('gender'),
            mobile:           value('mobile'),
            email:            value('email'),
            addressLine:      isLocal ? '' : value('addressLine'),
            locality:         isLocal ? '' : value('locality'),
            areaId:           area.id,
            areaName:         area.name,
            postalCode:       isLocal ? '' : value('postalCode'),
            notes:            value('notes'),
            connectionSource: value('connectionSource'),
            firstVisitOn:     value('firstVisitOn'),
            priority:         value('priority'),
            campusId:         value('campus'),
            isLocal:          isLocal,
            startFollowUp:    document.getElementById('startFollowUp').checked,
            autoAssign:       assignNowChosen(),

            // Only meaningful when somebody already holds the number. Sent as null
            // otherwise: the server refuses a relationship on a number nobody holds,
            // because there would be no one to be related to.
            relationshipCode: baseVisitor ? value('relationshipCode') : ''
        };
    }

    /**
     * The two modes send contacts DIFFERENTLY, and that is the whole point.
     *
     * Intake builds a `contacts` array, which is how a person is created. An update
     * does not bind that shape at all — it takes `mobile` and `email` directly — so
     * a correction that sent the array had its number and email dropped in silence
     * while the save still came back successful. That was the bug this addresses: the
     * one field an operator most often opens this screen to fix was the one field
     * that never saved.
     */
    function buildPersonRequest(p, correcting) {
        var request = {
            givenName: p.givenName,
            isLocal: p.isLocal
        };

        if (correcting) {
            request.mobile = p.mobile;

            // Always sent, empty included: an empty string is how an email that was
            // wrongly entered gets removed. Omitting it would leave it on file.
            request.email = p.email;

            // Not on this form, but on the record. An update stores exactly what it
            // is sent, so this is echoed back rather than blanked.
            if (editing && editing.householdType) request.householdType = editing.householdType;
        } else {
            var contacts = [{ contactType: 'MOBILE', value: p.mobile, isPrimary: true }];

            if (p.email) contacts.push({ contactType: 'EMAIL', value: p.email, isPrimary: false });

            request.contacts = contacts;
        }

        // Who they are to the family already on this number. Never sent on a
        // correction: an update changes an existing person and cannot move them into
        // somebody's household.
        if (!correcting && p.relationshipCode) request.relationshipCode = p.relationshipCode;

        // Only send what was filled in; empty strings would overwrite with blanks.
        if (p.familyName)  request.familyName = p.familyName;
        if (p.ageBand)     request.ageBand = p.ageBand;
        if (p.gender)      request.gender = p.gender;
        if (p.addressLine) request.addressLine = p.addressLine;
        if (p.locality)    request.locality = p.locality;
        if (p.postalCode)  request.postalCode = p.postalCode;

        // Both are sent. The id is what was picked from the list; the name is what
        // is in the box. The server prefers the id and falls back to the name,
        // creating the area when nothing matches — that find-or-create is deliberately
        // NOT done here as a separate call, because two operators typing the same new
        // area at once would race and one of them would get a failure instead of a save.
        if (p.areaId)      request.areaId = p.areaId;
        if (p.areaName)    request.areaName = p.areaName;
        if (p.notes)       request.notes = p.notes;

        // Omitted when the picker is hidden, which lets the server fall back to the
        // operator's own campus rather than this screen guessing at one — and when
        // correcting a record whose campus this operator has no option for, so the
        // save leaves it where it is instead of moving it.
        if (p.campusId && !(correcting && editing && editing.keepCampus)) {
            request.campusId = p.campusId;
        }

        return request;
    }

    function validate(p) {
        if (!p.givenName) {
            return { field: 'givenName', message: 'Enter the visitor\'s first name.' };
        }

        if (!p.mobile) {
            return { field: 'mobile', message: 'Enter a mobile number.' };
        }

        // Asked only when somebody already holds the number, and then it is the whole
        // point of the prompt. The server refuses the save without it too, so this
        // only saves a round trip.
        if (baseVisitor && !p.relationshipCode) {
            return {
                field: 'relationshipCode',
                message: 'Say how this visitor is related to ' + baseVisitor.baseVisitorName + '.'
            };
        }

        if (!MobileInput.isValid(p.mobile)) {
            return {
                field: 'mobile',
                message: MobileInput.problem(p.mobile) ||
                         'Enter a 10-digit Indian mobile number starting 6, 7, 8 or 9.'
            };
        }

        if (p.email && p.email.indexOf('@') === -1) {
            return { field: 'email', message: 'That does not look like a valid email address.' };
        }

        // Required only for someone local. They are followed up in person, and the
        // area is what decides which volunteer is close enough to take them — a
        // blank one leaves the case matchable to nobody in particular.
        if (p.isLocal && !p.areaName) {
            return {
                field: 'areaName',
                message: 'Enter the area they live in. Type it in full if it is not on the list yet.'
            };
        }

        return null;
    }

    /**
     * The server's refusal message names the matches but is written for a developer
     * ("resubmit with allowDuplicate"). An operator gets the fact, not the API
     * instruction — the names already came back, masked, from the pre-check.
     */
    /**
     * The save came back asking for a relationship, which means the blur check did
     * not run or the number was claimed in between. Fetch who holds it and open the
     * same prompt, so the operator answers one question rather than being told off.
     */
    function askRelationshipAfterRefusal(mobile, message) {
        showError(message || 'Say how this visitor is related to the person who already uses this number.');

        fetch(API_BASE_URL + '/people/base-visitor?mobile=' + encodeURIComponent(mobile))
            .then(function (res) { return res.json(); })
            .then(function (body) {
                var info = (body && body.data) || {};

                if (!info.relationshipRequired) return;

                showRelationship(info);
                markField('relationshipCode');
            })
            .catch(function () { /* the message already says what is needed */ });
    }

    function offerDuplicateOverride(message) {
        var names = extractNames(message);

        dupNotice.innerHTML =
            '<strong>This number is already on file.</strong>' +
            (names ? '<ul class="dup-list"><li>' + AdminShell.escapeHtml(names) + '</li></ul>'
                   : '<br>') +
            'Nothing has been saved. If this is the same person, there is nothing to do. ' +
            'If it really is someone different — a shared family phone, for example — press ' +
            '<strong>Save anyway</strong>.';

        show(dupNotice);

        duplicateAcknowledged = true;
        saveBtn.textContent = 'Save anyway';
        saveBtn.focus();
    }

    /**
     * Pulls the matched names out of the server's message, which formats them as
     * "... already recorded (Name, Other Name). ...". Best-effort only: if the
     * wording ever changes the notice simply omits the names rather than showing
     * a mangled string.
     */
    function extractNames(message) {
        var match = /\(([^)]+)\)/.exec(String(message || ''));
        return match ? match[1] : '';
    }

    function addToSession(person, followUpStarted, assignedTo) {
        recorded.unshift({
            name: (person && person.fullName) || '—',
            reference: (person && person.referenceCode) || '',
            followUp: followUpStarted,
            assignedTo: assignedTo || '',
            at: new Date()
        });

        renderSession();
    }

    function renderSession() {
        if (!recorded.length) {
            sessionList.innerHTML = '<p class="empty">Nothing recorded yet in this session.</p>';
            return;
        }

        sessionList.innerHTML = recorded.map(function (item) {
            var time = item.at.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });

            return '<div class="session-item">' +
                     '<div class="session-name">' + AdminShell.escapeHtml(item.name) + '</div>' +
                     '<div class="session-meta">' +
                       (item.reference ? AdminShell.escapeHtml(item.reference) + ' · ' : '') +
                       time + ' · ' +
                       (!item.followUp
                            ? 'no follow-up'
                            : item.assignedTo
                                ? 'assigned to ' + AdminShell.escapeHtml(item.assignedTo)
                                : 'queued for assignment') +
                     '</div>' +
                   '</div>';
        }).join('');
    }

    function resetForm(clearEverything) {
        var keepDate = document.getElementById('firstVisitOn').value;

        form.reset();

        document.getElementById('isLocal').checked = true;
        document.getElementById('startFollowUp').checked = true;
        document.getElementById('priorityField').hidden = false;

        // form.reset() empties the area input but knows nothing about the id the
        // picker is holding, which would then be sent with the NEXT visitor.
        if (areaPicker) areaPicker.clear();

        document.getElementById('localBlock').hidden = false;
        document.getElementById('awayBlock').hidden = true;

        // form.reset() puts the boxes back to their markup defaults, which is a state
        // the rules may not permit. Re-apply them before anything is on screen.
        syncFollowUp();

        // The next visitor is a different question. Leaving the prompt up would ask
        // for their relationship to somebody whose number has not been typed yet.
        hideRelationship();

        // The operator is usually entering a batch from the same service, so the
        // visit date carries over rather than being retyped every time.
        document.getElementById('firstVisitOn').value = clearEverything ? '' : keepDate;
        if (clearEverything) setToday();

        duplicateAcknowledged = false;
        saveBtn.textContent = defaultSaveLabel();

        hide(dupNotice);
        hide(formError);
        clearAllFieldErrors();
    }

    /** The button says which of the two things a save would do. */
    function defaultSaveLabel() {
        return editing ? 'Save correction' : 'Save visitor';
    }

    // ------------------------------------------------- correcting a record
    //
    // Data entry operators create these records, so they are the ones who mishear a
    // name or transpose a digit. Without this the only person who could fix it was a
    // volunteer or above, which in practice meant the record stayed wrong.
    //
    // The server allows exactly this much: GET /people/{id}/intake returns only the
    // fields this form collects, and PUT /people/{id} accepts only those. Lifecycle,
    // do-not-contact and deletion are other endpoints on stricter policies.

    function openFinder() {
        document.getElementById('findCard').hidden = false;
        document.getElementById('findTerm').focus();
    }

    function closeFinder() {
        document.getElementById('findCard').hidden = true;
        document.getElementById('findTerm').value = '';
        document.getElementById('findResults').innerHTML = '';
    }

    function searchPeople() {
        var term = document.getElementById('findTerm').value.trim();
        var host = document.getElementById('findResults');

        // Matches the server's own floor. Below it every search returns the same
        // empty answer, so asking is only noise.
        if (term.length < 3) { host.innerHTML = ''; return; }

        fetch(API_BASE_URL + '/people/lookup?q=' + encodeURIComponent(term))
            .then(function (res) { return res.json(); })
            .then(function (body) {
                var matches = (body && body.data) || [];

                if (!matches.length) {
                    host.innerHTML = '<p class="hint">Nobody on file matches that.</p>';
                    return;
                }

                host.innerHTML = '<ul class="dup-list">' + matches.map(function (m) {
                    return '<li>' +
                        AdminShell.escapeHtml(m.fullName) +
                        ' <span class="hint">' + AdminShell.escapeHtml(m.maskedContact || '') + '</span> ' +
                        '<button type="button" class="btn btn-sm" data-correct="' +
                            AdminShell.escapeHtml(m.id) + '">Correct</button>' +
                    '</li>';
                }).join('') + '</ul>';
            })
            .catch(function () {
                host.innerHTML = '<p class="hint">Could not search just now.</p>';
            });
    }

    function loadForCorrection(id) {
        fetch(API_BASE_URL + '/people/' + encodeURIComponent(id) + '/intake')
            .then(function (res) { return res.json(); })
            .then(function (body) {
                if (!body || body.responseType !== 0 || !body.data) {
                    showError((body && body.message) || 'That record could not be opened.');
                    return;
                }

                fillForm(body.data);
                startEditing(body.data);
                closeFinder();
            })
            .catch(function () { showError('Could not reach the server.'); });
    }

    function fillForm(p) {
        resetForm(true);

        setValue('givenName', p.givenName);
        setValue('familyName', p.familyName);
        setValue('ageBand', p.ageBand);
        setValue('gender', p.gender);
        setValue('mobile', p.mobile);
        setValue('email', p.email);
        setValue('addressLine', p.addressLine);
        setValue('locality', p.locality);
        setValue('postalCode', p.postalCode);
        setValue('notes', p.notes);
        setValue('campus', p.campusId);

        var isLocal = p.isLocal !== false;

        document.getElementById('isLocal').checked = isLocal;
        document.getElementById('localBlock').hidden = !isLocal;
        document.getElementById('awayBlock').hidden = isLocal;

        syncFollowUp();

        // The picker holds an id as well as the text, and setting only the text would
        // send a blank id and re-create the area by name on save.
        if (areaPicker && isLocal && (p.areaId || p.areaName)) {
            areaPicker.set(p.areaId, p.areaName || '');
        }
    }

    /**
     * Offers to put an already-recorded visitor onto the follow-up queue.
     *
     * Always offered while editing, because this screen cannot tell from
     * /people/{id}/intake whether a case exists — that endpoint returns intake fields
     * only, deliberately. The SERVER knows, and refuses with "already has an open
     * case", which is a better answer than a button this screen guessed at.
     */
    function showOpenCaseOffer() {
        var block = document.getElementById('openCaseBlock');
        if (!block) return;

        document.getElementById('openCaseText').textContent =
            'If this visitor was recorded without a follow-up case, nobody is going to ' +
            'contact them and the assignment job cannot see them. Opening a case here ' +
            'puts them on the queue.';

        document.getElementById('openCaseBtn').disabled = false;
        block.hidden = false;
    }

    function wireOpenCaseButton() {
        var btn = document.getElementById('openCaseBtn');
        if (!btn) return;

        btn.addEventListener('click', function () {
            if (!editing) return;

            btn.disabled = true;
            btn.textContent = 'Opening…';

            // autoAssign follows the same rule intake uses. This is a repair, and a
            // repair should land the visitor exactly where they would have been.
            fetch(API_BASE_URL + '/cases', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    personId: editing.id,
                    autoAssign: autoAssignOnIntake,
                    priority: 'NORMAL'
                })
            })
                .then(function (res) { return res.json().catch(function () { return null; }); })
                .then(function (body) {
                    btn.textContent = 'Open a follow-up case';

                    // responseType 0 == Success. A warning here is a real answer —
                    // "already has an open case" is the common one, and means there
                    // was nothing to repair.
                    if (!body || body.responseType !== 0) {
                        btn.disabled = false;
                        showToast((body && body.message) || 'The case could not be opened.',
                                  'warning');
                        return;
                    }

                    showToast(body.message || 'Case opened.', 'success');
                    document.getElementById('openCaseBlock').hidden = true;
                })
                .catch(function () {
                    btn.disabled = false;
                    btn.textContent = 'Open a follow-up case';
                    showToast('Could not reach the server.', 'error');
                });
        });
    }

    function setValue(id, v) {
        var el = document.getElementById(id);
        if (el) el.value = v == null ? '' : v;
    }

    /**
     * Whether a select can actually hold this value. Assigning one it has no option
     * for leaves the box showing something else entirely, which then gets saved.
     */
    function hasOption(id, value) {
        var el = document.getElementById(id);

        if (!el || !el.options) return false;
        if (value == null || value === '') return false;

        for (var i = 0; i < el.options.length; i++) {
            if (el.options[i].value === value) return true;
        }

        return false;
    }

    function startEditing(p) {
        editing = {
            id: p.id,
            rowVersion: p.rowVersion,
            name: p.givenName,

            // Carried on the record but not on this form. An update stores exactly
            // what it is sent, so this is echoed back on save — otherwise correcting
            // a misheard name silently blanked a field the operator never saw.
            householdType: p.householdType || '',

            // The campus box only lists campuses this operator may file against. When
            // the record belongs to another one the value cannot bind, and saving what
            // the box happens to show would MOVE the person; the save omits it instead
            // and the server leaves the campus alone.
            keepCampus: !hasOption('campus', p.campusId)
        };

        document.getElementById('pageTitle').textContent = 'Correct a record';
        document.getElementById('pageSub').textContent =
            'Fix what was entered. This changes the existing record and does not create a new one.';

        var notice = document.getElementById('editNotice');
        notice.textContent = 'You are correcting an existing record. Saving updates it.';
        notice.hidden = false;

        saveBtn.textContent = 'Save correction';
        document.getElementById('cancelEditBtn').hidden = false;

        // A follow-up belongs to recording somebody new. Hidden rather than merely
        // ignored, so nothing on screen suggests a correction might start one.
        var followUp = document.getElementById('startFollowUp');
        if (followUp) {
            followUp.checked = false;
            var wrap = followUp.closest('.field') || followUp.parentElement;
            if (wrap) wrap.hidden = true;
        }

        var assignField = document.getElementById('assignNowField');
        if (assignField) assignField.hidden = true;

        showOpenCaseOffer();

        // The note explains why no volunteer is assigned at intake, which is not the
        // question here — a correction does not open a case in the first place.
        document.getElementById('followUpNote').hidden = true;

        // Nor is a correction ever somebody new joining a household.
        hideRelationship();

        document.getElementById('priorityField').hidden = true;

        // "How they found us" and the visit date belong to the case, not the person,
        // so an update cannot store them. Hidden for the same reason as the follow-up
        // box: an editable field whose value is thrown away is worse than no field.
        setVisitFieldsVisible(false);

        window.scrollTo(0, 0);
    }

    function setVisitFieldsVisible(visible) {
        var fields = document.getElementById('visitFields');
        if (fields) fields.hidden = !visible;
    }

    function stopEditing() {
        editing = null;

        document.getElementById('pageTitle').textContent = 'Record a visitor';
        document.getElementById('pageSub').textContent =
            'Capture someone who visited so a volunteer can follow up with them. ' +
            'Only a name and one contact number are required.';

        hide(document.getElementById('editNotice'));

        saveBtn.textContent = 'Save visitor';
        document.getElementById('cancelEditBtn').hidden = true;

        var followUp = document.getElementById('startFollowUp');
        if (followUp) followUp.checked = true;

        var followUpField = document.getElementById('startFollowUpField');
        if (followUpField) followUpField.hidden = false;

        document.getElementById('openCaseBlock').hidden = true;

        var assignNow = document.getElementById('assignNow');
        if (assignNow) assignNow.checked = true;

        document.getElementById('priorityField').hidden = false;
        setVisitFieldsVisible(true);

        // Not simply "show it again": whether the box belongs on screen is the rules'
        // decision, and editing is null by now so syncFollowUp will make it.
        syncFollowUp();
    }

    function setBusy(busy, message) {
        saveBtn.disabled = busy;
        clearBtn.disabled = busy;
        statusHint.textContent = busy ? (message || '') : '';
    }

    function showError(message) {
        formError.textContent = message;
        formError.classList.add('notice-warn');
        show(formError);
        showToast(message, 'error');
    }

    function markField(id) {
        var el = document.getElementById(id);
        if (!el) return;

        el.setAttribute('aria-invalid', 'true');
        el.focus();
    }

    function clearFieldError(el) {
        if (el && el.removeAttribute) el.removeAttribute('aria-invalid');
    }

    function clearAllFieldErrors() {
        Array.prototype.forEach.call(
            form.querySelectorAll('[aria-invalid]'),
            function (el) { el.removeAttribute('aria-invalid'); });
    }

    function value(id) {
        var el = document.getElementById(id);
        return el ? el.value.trim() : '';
    }

    function show(el) { el.hidden = false; }
    function hide(el) { el.hidden = true; el.textContent = ''; }

})();
