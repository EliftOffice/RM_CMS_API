/* ============================================================================
   Team lead shell — renders the shared header on every team lead screen.

   Each screen used to carry its own header markup, and they had drifted: the
   dashboard had three buttons with inline margins, manual assignment had a bare
   text link, the escalation screen's navbar was commented out, and the check-in
   screen had no header at all. One renderer keeps them the same as they change.

   Usage: put <div id="tlShell"></div> at the top of the body and set
   window.TL_PAGE = 'dashboard' | 'assign' | 'escalation' | 'checkin' before
   this script loads (or pass it via data-page on the div).
   ========================================================================== */
(function (window) {
    'use strict';

    var ROOT = '/templates';

    /**
     * The team lead's menu.
     *
     * Add volunteer is deliberately NOT here. Enrolling a volunteer creates an
     * account that can read other people's pastoral records; that is a pastor or
     * administrator's decision, not something a team lead grants themselves.
     */
    var NAV = [
        { key: 'dashboard', label: 'My team',     href: ROOT + '/TeamLeads/TeamLeadDashboard.html' },
        { key: 'assign',    label: 'Assign cases', href: ROOT + '/Peoples/ManualAssignments.html' },
        { key: 'pipeline',  label: 'People',       href: ROOT + '/Peoples/Pipeline.html' }
    ];

    function escapeHtml(value) {
        return String(value === null || value === undefined ? '' : value)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    /**
     * Renders the header into #tlShell.
     *
     * @param {object} options
     *   title    — the screen's name
     *   subtitle — optional line beneath it
     *   page     — which NAV entry is current
     *   primary  — {label, href} rendered as the green button, for the one
     *              action this screen exists for. Omitted on most screens.
     */
    function render(options) {
        var mount = document.getElementById('tlShell');
        if (!mount) return;

        options = options || {};

        var page = options.page || mount.getAttribute('data-page') || window.TL_PAGE || '';
        var title = options.title || document.title.replace(/ [-–—] RM ?CMS.*$/i, '');

        var links = NAV.map(function (item) {
            // The screen you are on is marked rather than linked away to — a link
            // back to where you already are is a dead click.
            return item.key === page
                ? '<span class="tl-btn is-current">' + escapeHtml(item.label) + '</span>'
                : '<a class="tl-btn" href="' + item.href + '">' + escapeHtml(item.label) + '</a>';
        }).join('');

        var primary = options.primary
            ? '<a class="tl-btn-primary" href="' + options.primary.href + '">' +
                  escapeHtml(options.primary.label) + '</a>'
            : '';

        mount.className = 'tl-header';
        mount.innerHTML =
            '<div class="tl-brand">' +
                '<div class="tl-logo">' +
                    '<img src="' + ROOT + '/assets/Images/logo.jpeg" alt="RM" ' +
                         'onerror="this.style.display=\'none\';this.parentElement.textContent=\'R\'">' +
                '</div>' +
                '<div class="tl-titles">' +
                    '<div class="tl-title">' + escapeHtml(title) + '</div>' +
                    '<div class="tl-subtitle" id="tlSubtitle">' + escapeHtml(options.subtitle || '') + '</div>' +
                '</div>' +
            '</div>' +
            '<nav class="tl-nav">' +
                primary + links +
                '<a class="tl-btn-quiet" href="#" id="tlSignOut">Sign out</a>' +
            '</nav>';

        var signOut = document.getElementById('tlSignOut');

        if (signOut) {
            signOut.addEventListener('click', function (e) {
                e.preventDefault();

                // Clears the tokens and the refresh cookie. A plain link to the
                // login page left the session live on the server.
                if (window.RmAuth && RmAuth.logout) { RmAuth.logout(); return; }

                window.location.href = ROOT + '/Volunteers/Login.html';
            });
        }
    }

    /** Updates the subtitle after the page has loaded its data. */
    function setSubtitle(text) {
        var el = document.getElementById('tlSubtitle');
        if (el) el.textContent = text || '';
    }

    window.TeamLeadShell = { render: render, setSubtitle: setSubtitle, nav: NAV };

    // Render as soon as the DOM is ready, so the chrome is never the slow part.
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { render({}); });
    } else {
        render({});
    }
})(window);
