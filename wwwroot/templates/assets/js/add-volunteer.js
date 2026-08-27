/*
 * Add a volunteer — team lead and above.
 *
 * Replaces the legacy Volunteers.html + CreatingVolunteer.js, which posted to
 * /api/volunteers/CreateVolunteerAsync against tables the new schema dropped, and
 * which the team lead dashboard opened inside an iframe (blocked by
 * X-Frame-Options: DENY, so it rendered as "localhost refused to connect").
 *
 * Flow:
 *   GET  /api/people/picker?q=     -> find the person
 *   GET  /api/volunteers/reference -> capacity bands
 *   GET  /api/teams                -> team placement
 *   POST /api/volunteers           -> enrol
 *
 * Volunteers ATTACH to an existing person; they never create one. That is what
 * removed the MVP's five-way duplication of names and phone numbers, so this screen
 * searches rather than offering a name field.
 */
(function () {
    'use strict';

    var ROLES = ['ADMIN', 'PASTOR', 'TEAM_LEAD'];
    var SEARCH_DEBOUNCE_MS = 250;
    var MIN_QUERY = 2;

    var form, saveBtn, clearBtn, formError, statusHint, sessionList;
    var search, results, chosenField, chosenName, chosenSub;
    var selectedPerson = null;
    var searchTimer = null;
    var added = [];

    document.addEventListener('DOMContentLoaded', function () {
        AdminShell
            .boot({
                roles: ROLES,
                active: { href: '/templates/Volunteers/AddVolunteer.html', area: 'Volunteers' }
            })
            .then(function (ok) {
                if (!ok) return;
                document.getElementById('pageBody').hidden = false;
                init();
            });
    });

    function init() {
        form        = document.getElementById('volunteerForm');
        saveBtn     = document.getElementById('saveBtn');
        clearBtn    = document.getElementById('clearBtn');
        formError   = document.getElementById('formError');
        statusHint  = document.getElementById('statusHint');
        sessionList = document.getElementById('sessionList');
        search      = document.getElementById('personSearch');
        results     = document.getElementById('pickerResults');
        chosenField = document.getElementById('chosenField');
        chosenName  = document.getElementById('chosenName');
        chosenSub   = document.getElementById('chosenSub');

        loadBands();
        loadTeams();
        setToday();

        search.addEventListener('input', onSearchInput);
        search.addEventListener('keydown', onSearchKeys);
        document.getElementById('clearPerson').addEventListener('click', clearPerson);
        clearBtn.addEventListener('click', function () { resetForm(); });
        form.addEventListener('submit', onSubmit);

        // Close the result list when focus moves away, but not while a result is
        // being clicked — mousedown on a result fires before blur.
        document.addEventListener('click', function (e) {
            if (!document.getElementById('pickerField').contains(e.target)) hideResults();
        });
    }

    // ---------------------------------------------------------------- reference

    function loadBands() {
        fetch(API_BASE_URL + '/volunteers/reference')
            .then(function (r) { return r.json(); })
            .then(function (body) {
                var bands = (body && body.data && body.data.capacityBands) || [];
                var host = document.getElementById('bandOptions');

                if (!bands.length) {
                    host.innerHTML = '<p class="hint">No capacity bands are configured.</p>';
                    return;
                }

                host.innerHTML = bands.map(function (band, index) {
                    var code = band.code || band.Code;
                    var label = band.label || band.Label || code;
                    var min = band.minPerWeek != null ? band.minPerWeek : band.MinPerWeek;
                    var max = band.maxPerWeek != null ? band.maxPerWeek : band.MaxPerWeek;
                    var note = band.description || band.Description || '';

                    return '<label class="band-option">' +
                             '<input type="radio" name="band" value="' + AdminShell.escapeHtml(code) + '"' +
                               (index === 0 ? ' checked' : '') + '>' +
                             '<span class="band-text">' +
                               '<strong>' + AdminShell.escapeHtml(label) +
                                 ' — ' + min + ' to ' + max + ' a week</strong>' +
                               '<span class="sub">' + AdminShell.escapeHtml(note) + '</span>' +
                             '</span>' +
                           '</label>';
                }).join('');
            })
            .catch(function () {
                showError('Could not load capacity bands. Reload the page and try again.');
            });
    }

    function loadTeams() {
        fetch(API_BASE_URL + '/teams')
            .then(function (r) { return r.json(); })
            .then(function (body) {
                var teams = (body && body.data) || [];
                var select = document.getElementById('teamId');

                teams.forEach(function (team) {
                    var option = document.createElement('option');
                    option.value = team.id || team.Id;
                    option.textContent = team.name || team.Name;
                    select.appendChild(option);
                });
            })
            .catch(function () { /* team is optional; the form still works without it */ });
    }

    function setToday() {
        var now = new Date();
        document.getElementById('startedOn').value =
            now.getFullYear() + '-' +
            String(now.getMonth() + 1).padStart(2, '0') + '-' +
            String(now.getDate()).padStart(2, '0');
    }

    // ---------------------------------------------------------------- person picker

    function onSearchInput() {
        var q = search.value.trim();

        clearTimeout(searchTimer);

        if (q.length < MIN_QUERY) { hideResults(); return; }

        // Debounced: a request per keystroke would hit the global rate limiter.
        searchTimer = setTimeout(function () { runSearch(q); }, SEARCH_DEBOUNCE_MS);
    }

    function runSearch(q) {
        fetch(API_BASE_URL + '/people/picker?q=' + encodeURIComponent(q))
            .then(function (r) { return r.json(); })
            .then(function (body) {
                var matches = (body && body.data) || [];

                if (!matches.length) {
                    results.innerHTML =
                        '<div class="picker-item"><span class="sub">' +
                        'Nobody matches. Record them on the visitor screen first.' +
                        '</span></div>';
                    showResults();
                    return;
                }

                results.innerHTML = matches.map(function (m, i) {
                    return '<div class="picker-item" data-index="' + i + '">' +
                             AdminShell.escapeHtml(m.fullName) +
                             '<div class="sub">' +
                               AdminShell.escapeHtml(m.maskedContact || '') +
                               (m.lifecycleStatus ? ' · ' + AdminShell.escapeHtml(m.lifecycleStatus) : '') +
                             '</div>' +
                           '</div>';
                }).join('');

                Array.prototype.forEach.call(results.querySelectorAll('.picker-item'), function (el) {
                    var index = parseInt(el.getAttribute('data-index'), 10);
                    if (isNaN(index)) return;

                    el.addEventListener('mousedown', function (e) {
                        e.preventDefault();
                        choosePerson(matches[index]);
                    });
                });

                showResults();
            })
            .catch(function () { hideResults(); });
    }

    function onSearchKeys(e) {
        if (e.key === 'Escape') hideResults();
    }

    function choosePerson(person) {
        selectedPerson = person;

        chosenName.textContent = person.fullName;
        chosenSub.textContent = [person.maskedContact, person.lifecycleStatus]
            .filter(Boolean).join(' · ');

        chosenField.hidden = false;
        document.getElementById('pickerField').hidden = true;

        hideResults();
        clearFieldError(search);
    }

    function clearPerson() {
        selectedPerson = null;
        chosenField.hidden = true;
        document.getElementById('pickerField').hidden = false;
        search.value = '';
        search.focus();
    }

    function showResults() { results.hidden = false; }
    function hideResults() { results.hidden = true; }

    // ---------------------------------------------------------------- submit

    function onSubmit(event) {
        event.preventDefault();
        hide(formError);

        if (!selectedPerson) {
            showError('Search for and select the person first.');
            markField(search);
            return;
        }

        var band = form.querySelector('input[name="band"]:checked');

        if (!band) {
            showError('Choose how much this volunteer can take on.');
            return;
        }

        var request = {
            personId: selectedPerson.id,
            capacityBandCode: band.value
        };

        var teamId = document.getElementById('teamId').value;
        if (teamId) request.teamId = teamId;

        var startedOn = document.getElementById('startedOn').value;
        if (startedOn) request.startedOn = startedOn;

        setBusy(true, 'Adding…');

        fetch(API_BASE_URL + '/volunteers', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(request)
        })
            .then(function (r) { return r.json(); })
            .then(function (body) {
                setBusy(false);

                // responseType: 0 Success, 1 Warning, 2 Error.
                if (!body || body.responseType !== 0) {
                    showError((body && body.message) || 'Could not add this volunteer.');
                    return;
                }

                var volunteer = body.data || {};
                var name = volunteer.fullName || selectedPerson.fullName;

                showToast(name + ' added as a volunteer.', 'success');
                addToSession(volunteer, name);
                resetForm();
            })
            .catch(function () {
                setBusy(false);
                showError('Could not reach the server. Check your connection and try again.');
            });
    }

    // ---------------------------------------------------------------- helpers

    function addToSession(volunteer, name) {
        added.unshift({
            name: name,
            reference: volunteer.referenceCode || '',
            band: volunteer.capacityBandCode || volunteer.capacityBandLabel || '',
            at: new Date()
        });

        renderSession();
    }

    function renderSession() {
        if (!added.length) {
            sessionList.innerHTML = '<p class="empty">Nothing added yet in this session.</p>';
            return;
        }

        sessionList.innerHTML = added.map(function (item) {
            var time = item.at.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });

            return '<div class="session-item">' +
                     '<div class="session-name">' + AdminShell.escapeHtml(item.name) + '</div>' +
                     '<div class="session-meta">' +
                       (item.reference ? AdminShell.escapeHtml(item.reference) + ' · ' : '') +
                       (item.band ? AdminShell.escapeHtml(item.band) + ' · ' : '') + time +
                     '</div>' +
                   '</div>';
        }).join('');
    }

    function resetForm() {
        clearPerson();

        var first = form.querySelector('input[name="band"]');
        if (first) first.checked = true;

        document.getElementById('teamId').value = '';
        setToday();

        hide(formError);
        clearFieldError(search);
    }

    function setBusy(busy, message) {
        saveBtn.disabled = busy;
        clearBtn.disabled = busy;
        statusHint.textContent = busy ? (message || '') : '';
    }

    function showError(message) {
        formError.textContent = message;
        show(formError);
        showToast(message, 'error');
    }

    function markField(el) { if (el) { el.setAttribute('aria-invalid', 'true'); el.focus(); } }
    function clearFieldError(el) { if (el) el.removeAttribute('aria-invalid'); }

    function show(el) { el.hidden = false; }
    function hide(el) { el.hidden = true; el.textContent = ''; }

})();
