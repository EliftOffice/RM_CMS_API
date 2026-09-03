/* ============================================================================
   Pastor shell — renders the shared header on every pastor screen.

   Mirrors teamlead-shell.js: same #tlShell mount point and the same
   teamlead-shell.css classes, because a pastor's own screens (escalations,
   check-ins, the people pipeline) are already styled with that palette — a
   second, differently-branded header for the one screen that is genuinely
   theirs (the dashboard) would make the pastor's own cluster of pages look
   like two different applications.

   Usage: put <div id="tlShell"></div> at the top of the body and set
   window.TL_PAGE = 'dashboard' before this script loads.
   ========================================================================== */
(function (window) {
    'use strict';

    var ROOT = '/templates';

    /**
     * The pastor's menu.
     *
     * Escalations and Check-ins point at the same screens a team lead uses —
     * there is no separate pastor-only version of either, because the data on
     * them is already scoped server-side to whatever the signed-in account is
     * allowed to see (PolicyNames.TeamLeadOrAbove, which a pastor satisfies).
     *
     * Teams is included unconditionally, matching admin-shell.js's own choice:
     * the link showing is not the grant. The screen asks /api/teams/access on
     * load and explains itself when team.manage_by_pastor has not been turned
     * on, which reads better than a nav item that silently disappears.
     */
    var NAV = [
        { key: 'dashboard',   label: 'Dashboard',   href: ROOT + '/Pastor/Dashboard.html' },
        { key: 'escalations', label: 'Escalations', href: ROOT + '/TeamLeads/Escalations.html' },
        { key: 'checkins',    label: 'Check-ins',   href: ROOT + '/TeamLeads/CheckIns.html' },
        { key: 'pipeline',    label: 'People',      href: ROOT + '/Peoples/Pipeline.html' },
        { key: 'teams',       label: 'Teams',       href: ROOT + '/Admin/teams.html' }
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
     */
    function render(options) {
        var mount = document.getElementById('tlShell');
        if (!mount) return;

        options = options || {};

        var page = options.page || mount.getAttribute('data-page') || window.TL_PAGE || '';
        var title = options.title || document.title.replace(/ [-–—] RM ?CMS.*$/i, '');

        var links = NAV.map(function (item) {
            return item.key === page
                ? '<span class="tl-btn is-current">' + escapeHtml(item.label) + '</span>'
                : '<a class="tl-btn" href="' + item.href + '">' + escapeHtml(item.label) + '</a>';
        }).join('');

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
                links +
                '<a class="tl-btn-quiet" href="#" id="tlSignOut">Sign out</a>' +
            '</nav>';

        var signOut = document.getElementById('tlSignOut');

        if (signOut) {
            signOut.addEventListener('click', function (e) {
                e.preventDefault();

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

    window.PastorShell = { render: render, setSubtitle: setSubtitle, nav: NAV };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { render({}); });
    } else {
        render({});
    }
})(window);
