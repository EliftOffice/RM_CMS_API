/*
 * Visitor intake — the data-entry operator's screen.
 *
 * Deliberately narrow. A DATA_ENTRY account can record a visitor and check for
 * duplicates; it cannot list people or browse cases (GET /api/people is
 * VolunteerOrAbove). So this is a capture form, not a CRUD console, and the
 * "recorded just now" panel is built from what this session created rather than
 * by reading the list back.
 *
 * Flow:
 *   POST /api/people           -> create the person
 *   POST /api/cases            -> open a case so a volunteer follows up  (optional)
 *   GET  /api/people/lookup    -> warn about an existing record before saving
 *   GET  /api/areas/options    -> the area type-ahead (via AreaPicker)
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

    document.addEventListener('DOMContentLoaded', function () {
        AdminShell
            .boot({
                roles: ROLES,
                active: { href: '/templates/Peoples/PeopleEntry.html', area: 'Intake' }
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
        loadCampuses();
        setToday();
        wireFollowUpToggle();
        wireResidence();

        form.addEventListener('submit', onSubmit);
        clearBtn.addEventListener('click', function () { resetForm(true); });

        // Any edit invalidates a duplicate warning that was about the previous values.
        form.addEventListener('input', function (e) {
            clearFieldError(e.target);

            if (duplicateAcknowledged || !dupNotice.hidden) {
                duplicateAcknowledged = false;
                hide(dupNotice);
                saveBtn.textContent = 'Save visitor';
            }
        });

        // Digits-only, ten of them, starting 6-9 — enforced as they type.
        MobileInput.attach(document.getElementById('mobile'), {
            hint: document.getElementById('mobileHint'),
            hintText: '10 digits, starting 6-9. Checked against existing records ' +
                      'as you leave the field.'
        });

        document.getElementById('mobile').addEventListener('blur', checkForDuplicate);
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
                var bands = (body && body.data && body.data.ageBands) || [];
                var select = document.getElementById('ageBand');

                bands.forEach(function (code) {
                    var option = document.createElement('option');
                    option.value = code;
                    option.textContent = labelForBand(code);
                    select.appendChild(option);
                });
            })
            .catch(function () {
                showToast('Could not load the age-group list.', 'warning');
            });
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
        }

        toggle.addEventListener('change', sync);
        sync();
    }

    function wireFollowUpToggle() {
        var toggle = document.getElementById('startFollowUp');
        var priorityField = document.getElementById('priorityField');

        function sync() { priorityField.hidden = !toggle.checked; }

        toggle.addEventListener('change', sync);
        sync();
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

        fetch(API_BASE_URL + '/people/lookup?q=' + encodeURIComponent(mobile))
            .then(function (res) { return res.json(); })
            .then(function (body) {
                var matches = (body && body.data) || [];
                if (!matches.length) return;

                var names = matches.map(function (m) {
                    return '<li>' + AdminShell.escapeHtml(m.fullName) +
                           ' <span class="hint">' + AdminShell.escapeHtml(m.maskedContact || '') +
                           '</span></li>';
                }).join('');

                dupNotice.innerHTML =
                    '<strong>This number is already on file.</strong>' +
                    '<ul class="dup-list">' + names + '</ul>' +
                    'Check it is not the same person before saving.';

                show(dupNotice);
            })
            .catch(function () { /* the save-time check is the one that counts */ });
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

        var request = buildPersonRequest(payload);
        request.allowDuplicate = duplicateAcknowledged;

        var httpOk = true;

        fetch(API_BASE_URL + '/people', {
            method: 'POST',
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
                    if (body.code === 'duplicate_contact') {
                        offerDuplicateOverride(body.message);
                    } else {
                        showError(body.message || 'This visitor could not be saved.');
                    }

                    return;
                }

                var person = body.data;

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
            autoAssign: true,
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

                finish(person, true);
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

    function finish(person, followUpStarted, warning) {
        setBusy(false);

        var name = (person && person.fullName) || 'Visitor';

        if (warning) {
            showToast(warning, 'warning');
            showError(name + ' was saved, but follow-up did not start. ' +
                      'Tell a team lead so it is picked up manually.');
        } else {
            showToast(name + ' recorded' + (followUpStarted ? ' and follow-up started.' : '.'),
                      'success');
        }

        addToSession(person, followUpStarted);
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
            startFollowUp:    document.getElementById('startFollowUp').checked
        };
    }

    function buildPersonRequest(p) {
        var contacts = [{ contactType: 'MOBILE', value: p.mobile, isPrimary: true }];

        if (p.email) contacts.push({ contactType: 'EMAIL', value: p.email, isPrimary: false });

        var request = {
            givenName: p.givenName,
            isLocal: p.isLocal,
            contacts: contacts
        };

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
        // operator's own campus rather than this screen guessing at one.
        if (p.campusId)    request.campusId = p.campusId;

        return request;
    }

    function validate(p) {
        if (!p.givenName) {
            return { field: 'givenName', message: 'Enter the visitor\'s first name.' };
        }

        if (!p.mobile) {
            return { field: 'mobile', message: 'Enter a mobile number.' };
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

    function addToSession(person, followUpStarted) {
        recorded.unshift({
            name: (person && person.fullName) || '—',
            reference: (person && person.referenceCode) || '',
            followUp: followUpStarted,
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
                       (item.followUp ? 'follow-up started' : 'no follow-up') +
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

        // The operator is usually entering a batch from the same service, so the
        // visit date carries over rather than being retyped every time.
        document.getElementById('firstVisitOn').value = clearEverything ? '' : keepDate;
        if (clearEverything) setToday();

        duplicateAcknowledged = false;
        saveBtn.textContent = 'Save visitor';

        hide(dupNotice);
        hide(formError);
        clearAllFieldErrors();
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
