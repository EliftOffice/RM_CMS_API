/*
 * Events — the church calendar the public website lists.
 *
 *   GET  /api/events                the list, drafts included
 *   GET  /api/events/{id}           one event
 *   POST /api/events                create (always as a draft)
 *   PUT  /api/events/{id}           edit
 *   PUT  /api/events/{id}/status    publish, unpublish, cancel
 *   POST /api/events/{id}/poster    upload the poster (multipart)
 *   DELETE /api/events/{id}/poster  take the poster off again
 *
 * ADMIN, PASTOR and WEB_COORDINATOR.
 *
 * THE POSTER IS AN UPLOAD, NOT A DROPDOWN.
 * It used to be a slug chosen from GET /api/events/images — about twenty stock
 * plates baked into the website at build time. So the poster actually designed
 * for the event, the one on the flyer and the WhatsApp forward, was never one of
 * the choices, and two unrelated events routinely showed the same photograph.
 *
 * Saving is two requests when a picture was chosen: the event first, then the
 * poster against the id that comes back. It has to be that way round on create,
 * because there is no id to attach an upload to until the event exists.
 *
 * Two rules this screen exists to make obvious:
 *
 *   A new event is a DRAFT. Publishing is a separate button, so a half-typed
 *   event cannot reach the public site on a mis-click.
 *
 *   Times are LOCAL to the campus. The server converts to UTC and back; nothing
 *   here does date arithmetic, because a browser in another timezone would get a
 *   different answer than the church means.
 */
$(function () {
    'use strict';

    var esc = AdminShell.escapeHtml;

    // Mirrors PosterImages.MaxBytes on the server. Checked here as well so a 10 MB
    // photograph is refused before it is uploaded rather than after.
    var MAX_POSTER_BYTES = 6 * 1024 * 1024;

    var state = {
        page: 1,
        pageSize: 50,
        status: '',
        search: '',
        includePast: true,
        editing: null,        // null = creating
        campuses: [],
        pickedFile: null,     // chosen this time round, not yet uploaded
        slugTouched: false     // stop auto-filling once the editor types their own
    };

    var debounce = null;

    // The rows currently on screen. A status change sends the row version from here,
    // so it is the one the operator is looking at.
    var lastItems = [];

    AdminShell.boot({
        roles: ['ADMIN', 'PASTOR', 'WEB_COORDINATOR'],
        active: { href: '/pages/admin/events.html', area: 'Events' }
    }).then(function (ok) {
        if (!ok) return;
        $('#pageBody').prop('hidden', false);
        loadReference();
        bind();
        load();
    });

    // ------------------------------------------------------------------ data

    function loadReference() {
        // Campus only matters when there is more than one. With a single campus the
        // field stays hidden and the event belongs to all of them, which is the same
        // thing when there is only one.
        $.ajax({ url: API_BASE_URL + '/campuses/options', method: 'GET' })
            .done(function (res) {
                state.campuses = (res && res.data) || [];

                $('#fCampus').append(state.campuses.map(function (c) {
                    return '<option value="' + esc(c.id) + '">' + esc(c.name) + '</option>';
                }).join(''));

                $('#campusField').prop('hidden', state.campuses.length < 2);
            })
            .fail(function () { $('#campusField').prop('hidden', true); });
    }

    function load() {
        $.ajax({
            url: API_BASE_URL + '/events',
            method: 'GET',
            data: {
                page: state.page,
                pageSize: state.pageSize,
                status: state.status,
                search: state.search,
                includePast: state.includePast
            }
        })
            .done(function (res) {
                // A refusal arrives as HTTP 200 with responseType 1.
                if (!res || res.responseType !== 0 || !res.data) {
                    setEmpty((res && res.message) || 'Could not load events.');
                    return;
                }

                renderCounts(res.data.summary || {});
                renderRows(res.data.items || []);
                renderPager(res.data);

                $('#resultCount').text(
                    res.data.totalCount + (res.data.totalCount === 1 ? ' event' : ' events'));
            })
            .fail(function (xhr) {
                setEmpty(xhr && xhr.status === 403
                    ? 'You do not have access to events.'
                    : 'Could not load events.');
            });
    }

    // ------------------------------------------------------------------ render

    function renderCounts(s) {
        var cards = [
            { key: 'PUBLISHED', label: 'Live',      value: s.published || 0, live: true },
            { key: '',          label: 'Upcoming',  value: s.upcoming || 0, readonly: true },
            { key: 'DRAFT',     label: 'Drafts',    value: s.draft || 0 },
            { key: 'CANCELLED', label: 'Cancelled', value: s.cancelled || 0 }
        ];

        $('#counts').html(cards.map(function (c) {
            // "Upcoming" is a slice of "Live", not a status — filtering by it would
            // mean something different from every other tile here.
            if (c.readonly) {
                return '<div class="count-card" style="cursor:default;">' +
                           '<div class="count-value">' + c.value + '</div>' +
                           '<div class="count-label">' + esc(c.label) + '</div>' +
                       '</div>';
            }

            return '<button type="button" class="count-card' +
                       (c.live ? ' is-live' : '') +
                       (state.status === c.key ? ' is-active' : '') +
                       '" data-status="' + esc(c.key) + '">' +
                       '<div class="count-value">' + c.value + '</div>' +
                       '<div class="count-label">' + esc(c.label) + '</div>' +
                   '</button>';
        }).join(''));
    }

    function renderRows(items) {
        // Held so a status button can send the row version the screen is showing,
        // rather than one re-fetched behind the operator's back.
        lastItems = items || [];

        if (!items.length) {
            setEmpty(state.search || state.status
                ? 'Nothing matches that.'
                : 'No events yet. Create one and it will appear on the website once published.');
            return;
        }

        $('#eventTable tbody').html(items.map(function (e) {
            return '<tr' + (e.hasFinished ? ' class="row-past"' : '') + '>' +
                '<td>' + thumbnail(e) + '</td>' +
                '<td class="when">' +
                    '<div class="when-date">' + esc(formatDate(e.startsAtLocal)) + '</div>' +
                    '<div class="when-time">' + esc(formatTime(e.startsAtLocal)) +
                        (e.endsAtLocal ? ' – ' + esc(formatTime(e.endsAtLocal)) : '') + '</div>' +
                '</td>' +
                '<td>' +
                    '<div class="cell-name">' + esc(e.title) + '</div>' +
                    '<div class="slug">/events/' + esc(e.slug) + '</div>' +
                '</td>' +
                '<td class="cell-sub">' + esc(e.venue) +
                    (e.campusName ? '<div class="cell-sub">' + esc(e.campusName) + '</div>' : '') +
                '</td>' +
                '<td>' + statusBadge(e.status, e.hasFinished) + '</td>' +
                '<td class="cell-actions">' +
                    statusButtons(e) +
                    '<button type="button" class="btn btn-sm" data-edit="' + esc(e.id) + '">Edit</button>' +
                '</td>' +
            '</tr>';
        }).join(''));
    }

    /** The poster as the list shows it, or a placeholder saying there is none. */
    function thumbnail(e) {
        return e.posterUrl
            ? '<img class="poster-cell" src="' + esc(e.posterUrl) + '" alt="">'
            : '<div class="poster-cell-none">None</div>';
    }

    function statusBadge(status, finished) {
        if (status === 'PUBLISHED') {
            return finished
                ? '<span class="badge badge-off">Finished</span>'
                : '<span class="badge badge-ok">Live</span>';
        }

        if (status === 'CANCELLED') return '<span class="badge badge-warn">Cancelled</span>';

        return '<span class="badge badge-role">Draft</span>';
    }

    /**
     * Only the moves that make sense from where the event is. Offering "publish" on
     * something already live, or "cancel" on a draft nobody has seen, is offering a
     * button whose only outcome is confusion.
     */
    function statusButtons(e) {
        var id = esc(e.id);

        if (e.status === 'DRAFT') {
            return '<button type="button" class="btn btn-sm" data-publish="' + id + '">Publish</button>';
        }

        if (e.status === 'PUBLISHED') {
            return '<button type="button" class="btn btn-sm" data-unpublish="' + id + '">Unpublish</button>' +
                   (e.hasFinished ? '' :
                    '<button type="button" class="btn btn-sm btn-danger" data-cancel="' + id + '">Cancel</button>');
        }

        // Cancelled: the only way back is to publish it again.
        return '<button type="button" class="btn btn-sm" data-publish="' + id + '">Re-publish</button>';
    }

    function renderPager(d) {
        var pages = Math.max(1, Math.ceil(d.totalCount / d.pageSize));

        if (pages <= 1) { $('#pager').empty(); return; }

        $('#pager').html(
            '<button class="btn btn-sm" id="prevPage"' + (d.page <= 1 ? ' disabled' : '') + '>Previous</button>' +
            '<span class="pager-info">Page ' + d.page + ' of ' + pages + '</span>' +
            '<button class="btn btn-sm" id="nextPage"' + (d.page >= pages ? ' disabled' : '') + '>Next</button>');
    }

    // ------------------------------------------------------------------ editor

    function openEditor(row) {
        state.editing = row || null;
        state.slugTouched = !!row;   // an existing slug is never auto-rewritten

        var creating = !row;

        $('#editTitle').text(creating ? 'New event' : 'Edit ' + row.title);
        $('#editNotice').prop('hidden', true).text('');
        $('#liveWarn').prop('hidden', creating || row.status !== 'PUBLISHED');

        $('#fTitle').val(creating ? '' : row.title);
        $('#fSummary').val(creating ? '' : row.summary);
        $('#fDescription').val(creating ? '' : (row.description || ''));
        $('#fVenue').val(creating ? '' : row.venue);
        $('#fStarts').val(creating ? '' : row.startsAtLocal);
        $('#fEnds').val(creating ? '' : (row.endsAtLocal || ''));
        $('#fSlug').val(creating ? '' : row.slug);
        $('#fCampus').val(creating ? '' : (row.campusId || ''));

        state.pickedFile = null;
        $('#fPoster').val('');
        showPoster(creating ? null : row.posterUrl, creating ? null : posterLabel(row));

        $('#tzHint').text(creating
            ? 'Local time at the church.'
            : 'Local time at the church (' + (row.timezone || '') + ').');

        $('#editModal').addClass('open');
        $('#fTitle').trigger('focus');
    }

    function closeEditor() {
        $('#editModal').removeClass('open');
        state.editing = null;
        state.pickedFile = null;
    }

    // -------------------------------------------------------------- the poster

    function posterLabel(row) {
        if (!row || !row.hasPoster) return null;

        return (row.posterFileName || 'Poster') +
               ' · ' + Math.round((row.posterByteSize || 0) / 1024) + ' KB';
    }

    /**
     * Swaps the preview between a picture and the placeholder.
     *
     * `src` is either the served poster's URL or a data: URL for a file that has
     * only been chosen. A data: URL and not an object URL, because the page's
     * Content-Security-Policy allows `img-src 'self' data:` and says nothing about
     * blob: — an object URL here would simply not render, with no error anywhere.
     */
    function showPoster(src, label) {
        var has = !!src;

        $('#posterPreview').attr('src', src || '').prop('hidden', !has);
        $('#posterEmpty').prop('hidden', has);
        $('#posterMeta').text(label || '');

        // Only offered for a poster that is actually stored. Clearing one that was
        // merely picked is what the file input's own reset is for.
        $('#removePosterBtn').prop('hidden',
            !(state.editing && state.editing.hasPoster && !state.pickedFile));
    }

    function pickPoster(input) {
        var file = input.files && input.files[0];

        if (!file) { state.pickedFile = null; return; }

        if (file.size > MAX_POSTER_BYTES) {
            $(input).val('');
            state.pickedFile = null;
            notice('That image is larger than 6 MB. Choose a smaller one.');
            return;
        }

        // The server decides what a file really is by reading its bytes. This only
        // catches the obvious mistake early, with a clearer message than a refusal
        // after a five megabyte upload.
        if (!/^image\/(jpeg|png|webp)$/.test(file.type)) {
            $(input).val('');
            state.pickedFile = null;
            notice('Choose a JPEG, PNG or WebP image.');
            return;
        }

        state.pickedFile = file;

        var reader = new FileReader();

        reader.onload = function () {
            showPoster(reader.result,
                file.name + ' · ' + Math.round(file.size / 1024) + ' KB');
        };

        reader.readAsDataURL(file);
    }

    /**
     * The one request on this page that is not JSON.
     *
     * Content-Type is deliberately left unset: the browser has to write it itself so
     * it can append the multipart boundary, and naming it here produces a body the
     * server cannot parse. `processData: false` and `contentType: false` are what
     * stop jQuery from helpfully doing both.
     */
    function uploadPoster(eventId, file) {
        var form = new FormData();
        form.append('file', file, file.name);

        return $.ajax({
            url: API_BASE_URL + '/events/' + encodeURIComponent(eventId) + '/poster',
            method: 'POST',
            data: form,
            processData: false,
            contentType: false
        });
    }

    function removePoster() {
        if (!state.editing || !state.editing.hasPoster) return;

        $.ajax({
            url: API_BASE_URL + '/events/' + encodeURIComponent(state.editing.id) + '/poster',
            method: 'DELETE'
        })
            .done(function (res) {
                if (!res || res.responseType !== 0) {
                    notice((res && res.message) || 'The poster could not be removed.');
                    return;
                }

                // The row version moved with the delete, so the editor takes the fresh
                // record — saving with the stale one would be refused as a conflict.
                state.editing = res.data;
                state.pickedFile = null;
                $('#fPoster').val('');
                showPoster(null, null);
                load();
            })
            .fail(function (xhr) { notice(describeFailure(xhr)); });
    }

    function save() {
        var body = {
            title: $.trim($('#fTitle').val()),
            summary: $.trim($('#fSummary').val()),
            description: $.trim($('#fDescription').val()) || null,
            venue: $.trim($('#fVenue').val()),
            startsAt: $('#fStarts').val(),
            endsAt: $('#fEnds').val() || null,
            slug: $.trim($('#fSlug').val()) || null,
            campusId: $('#fCampus').val() || null
        };

        // Checked here so the editor is told before the round trip, not after it.
        if (!body.title) return notice('Give the event a title.');
        if (!body.summary) return notice('Write a one-line summary.');
        if (!body.venue) return notice('Say where it is.');
        if (!body.startsAt) return notice('Say when it starts.');

        var creating = !state.editing;
        var url = API_BASE_URL + '/events' + (creating ? '' : '/' + encodeURIComponent(state.editing.id));

        if (!creating) body.rowVersion = state.editing.rowVersion;

        var $btn = $('#saveBtn').prop('disabled', true).text('Saving…');

        $.ajax({
            url: url,
            method: creating ? 'POST' : 'PUT',
            contentType: 'application/json',
            data: JSON.stringify(body)
        })
            .done(function (res) {
                if (!res || res.responseType !== 0) {
                    notice((res && res.message) || 'That could not be saved.');
                    restore();
                    return;
                }

                if (!state.pickedFile) { finish(res.message); return; }

                // The event is stored; now the picture. A failed upload does NOT roll
                // the event back — it is saved, and saying so while pointing at the
                // image that did not go up beats pretending nothing happened and
                // losing everything that was typed.
                uploadPoster(res.data.id, state.pickedFile)
                    .done(function (up) {
                        if (!up || up.responseType !== 0) {
                            notice('The event was saved, but the poster was not: ' +
                                ((up && up.message) || 'the upload was refused.'));
                            restore();
                            load();
                            return;
                        }

                        finish(res.message);
                    })
                    .fail(function (xhr) {
                        notice('The event was saved, but the poster could not be uploaded. ' +
                               describeFailure(xhr));
                        restore();
                        load();
                    });
            })
            .fail(function (xhr) {
                notice(describeFailure(xhr));
                restore();
            });

        function finish(message) {
            restore();
            showToast(message || 'Saved.', 'success');
            closeEditor();
            load();
        }

        function restore() {
            $btn.prop('disabled', false).text('Save');
        }
    }

    function setStatus(id, status, confirmMessage) {
        var row = findRow(id);
        if (!row) return;

        if (confirmMessage && !window.confirm(confirmMessage)) return;

        $.ajax({
            url: API_BASE_URL + '/events/' + encodeURIComponent(id) + '/status',
            method: 'PUT',
            contentType: 'application/json',
            data: JSON.stringify({ status: status, rowVersion: row.rowVersion })
        })
            .done(function (res) {
                if (!res || res.responseType !== 0) {
                    showToast((res && res.message) || 'That could not be changed.', 'warning');
                    return;
                }

                showToast(res.message, 'success');
                load();
            })
            .fail(function (xhr) { showToast(describeFailure(xhr), 'error'); });
    }

    function findRow(id) {
        return lastItems.filter(function (e) { return e.id === id; })[0];
    }

    // ------------------------------------------------------------------ wiring

    function bind() {
        $('#newBtn').on('click', function () { openEditor(null); });

        $('#search').on('input', function () {
            var value = this.value;
            clearTimeout(debounce);
            debounce = setTimeout(function () {
                state.search = value;
                state.page = 1;
                load();
            }, 300);
        });

        $('#includePast').on('change', function () {
            state.includePast = this.checked;
            state.page = 1;
            load();
        });

        $('#clearBtn').on('click', function () {
            state.status = '';
            state.search = '';
            state.page = 1;
            state.includePast = true;
            $('#search').val('');
            $('#includePast').prop('checked', true);
            load();
        });

        $(document).on('click', '[data-status]', function () {
            var status = $(this).data('status') || '';
            state.status = (state.status === status) ? '' : status;
            state.page = 1;
            load();
        });

        $(document).on('click', '[data-edit]', function () {
            var row = findRow($(this).data('edit'));
            if (row) openEditor(row);
        });

        $(document).on('click', '[data-publish]', function () {
            setStatus($(this).data('publish'), 'PUBLISHED');
        });

        $(document).on('click', '[data-unpublish]', function () {
            setStatus($(this).data('unpublish'), 'DRAFT',
                'Take this off the website? Anyone holding the link will stop being able to see it.');
        });

        $(document).on('click', '[data-cancel]', function () {
            setStatus($(this).data('cancel'), 'CANCELLED',
                'Mark this event as cancelled? It stays on the website, shown as cancelled, ' +
                'so anyone who saw it advertised is told rather than hitting a dead link.');
        });

        $(document).on('click', '#prevPage', function () { state.page--; load(); });
        $(document).on('click', '#nextPage', function () { state.page++; load(); });
        $(document).on('click', '[data-close]', closeEditor);
        $('#saveBtn').on('click', save);

        $('#fPoster').on('change', function () { pickPoster(this); });
        $('#removePosterBtn').on('click', removePoster);

        // The web address follows the title until somebody edits it by hand. After
        // that it is theirs — retyping the title must not silently move the page.
        $('#fTitle').on('input', function () {
            if (state.slugTouched) return;
            $('#fSlug').val(slugify(this.value));
        });

        $('#fSlug').on('input', function () {
            state.slugTouched = true;
            this.value = slugify(this.value);
        });

        $('#editModal').on('click', function (e) { if (e.target === this) closeEditor(); });
    }

    // ------------------------------------------------------------------ helpers

    /** Mirrors EventSlugs.From on the server. The server is still the authority. */
    function slugify(value) {
        return String(value || '')
            .toLowerCase()
            .replace(/[^a-z0-9]+/g, '-')
            .replace(/^-+|-+$/g, '')
            .slice(0, 120);
    }

    function setEmpty(message) {
        lastItems = [];
        $('#eventTable tbody').html(
            '<tr><td colspan="6" class="empty">' + esc(message) + '</td></tr>');
        $('#pager').empty();
    }

    /**
     * A model-validation failure is a ProblemDetails whose title is the useless
     * "One or more validation errors occurred" — the part naming the field is in
     * `errors`.
     */
    function describeFailure(xhr) {
        var b = xhr && xhr.responseJSON;

        if (!b) {
            return xhr && xhr.status === 0
                ? 'Could not reach the server.'
                : 'That could not be saved.';
        }

        if (b.errors) {
            var messages = [];

            Object.keys(b.errors).forEach(function (field) {
                (b.errors[field] || []).forEach(function (m) {
                    if (messages.indexOf(m) === -1) messages.push(m);
                });
            });

            if (messages.length) return messages.join(' ');
        }

        return b.message || b.detail || 'That could not be saved.';
    }

    function notice(message) {
        $('#editNotice').text(message).prop('hidden', false);
    }

    /**
     * The server sends local time already formatted as "2027-04-02T08:00", so these
     * split the string rather than constructing a Date. Parsing it would apply the
     * BROWSER's timezone and shift an event by hours for anyone travelling.
     */
    function formatDate(local) {
        if (!local) return '—';

        var parts = String(local).split('T')[0].split('-');
        if (parts.length !== 3) return local;

        var months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun',
                      'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

        return parseInt(parts[2], 10) + ' ' + months[parseInt(parts[1], 10) - 1] + ' ' + parts[0];
    }

    function formatTime(local) {
        if (!local) return '';

        var time = String(local).split('T')[1];
        if (!time) return '';

        var bits = time.split(':');
        var hour = parseInt(bits[0], 10);
        var suffix = hour >= 12 ? 'pm' : 'am';
        var display = hour % 12 === 0 ? 12 : hour % 12;

        return display + (bits[1] === '00' ? '' : ':' + bits[1]) + suffix;
    }
});
