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

    // Page paths are absolute. They used to be built from a ROOT base, which
    // hid them from a project-wide search — when the pages moved, every link
    // here survived the rename untouched and silently pointed at nothing.

    /**
     * The team lead's menu.
     *
     * Add volunteer is deliberately NOT here. Enrolling a volunteer creates an
     * account that can read other people's pastoral records; that is a pastor or
     * administrator's decision, not something a team lead grants themselves.
     */
    var NAV = [
        { key: 'dashboard', label: 'My team',     href: '/pages/dashboard/team-lead.html' },
        { key: 'assign',    label: 'Assign cases', href: '/pages/care/assign-cases.html' },
        { key: 'pipeline',  label: 'People',       href: '/pages/care/pipeline.html' }
    ];

    function escapeHtml(value) {
        return String(value === null || value === undefined ? '' : value)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    /** True for a pastor who does not also lead a team of their own. */
    function isPastorNotLead() {
        var roles = (window.RmAuth && RmAuth.roleCodes()) || [];
        return roles.indexOf('TEAM_LEAD') === -1 && roles.indexOf('PASTOR') !== -1;
    }

    /** Whichever dashboard actually belongs to the signed-in account. */
    function dashboardHref() {
        return isPastorNotLead() ? ('/pages/dashboard/pastor.html') : ('/pages/dashboard/team-lead.html');
    }

    /**
     * Escalations.html and CheckIns.html each carry a "Back to dashboard" link
     * and a Cancel button, both hardcoded to the team lead dashboard because
     * every visitor to this shell used to be a team lead. Now a pastor reaches
     * these same screens from their own dashboard's nav, and following either
     * link sent them into a team lead's dashboard that is not theirs — scoped
     * to a team they do not lead, showing them someone else's queue.
     *
     * Rewritten here, once the signed-in account's role is actually known,
     * rather than in each page's own script: any screen that loads this shell
     * and links back to "the dashboard" gets the fix for free.
     */
    function fixDashboardLinks() {
        var href = dashboardHref();

        // Suffix match only. Every link in the pages is an absolute path now, so
        // the old bare-filename form this also tested can no longer occur.
        document.querySelectorAll('a[href$="/dashboard/team-lead.html"]')
            .forEach(function (a) { a.setAttribute('href', href); });
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
                    '<img src="/assets/img/logo.jpeg" alt="RM" ' +
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

                window.location.href = '/pages/auth/login.html';
            });
        }
    }

    /** Updates the subtitle after the page has loaded its data. */
    function setSubtitle(text) {
        var el = document.getElementById('tlSubtitle');
        if (el) el.textContent = text || '';
    }

    window.TeamLeadShell = {
        render: render,
        setSubtitle: setSubtitle,
        nav: NAV,
        dashboardHref: dashboardHref
    };

    // Render as soon as the DOM is ready, so the chrome is never the slow part.
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { render({}); });
    } else {
        render({});
    }

    // The role is not known yet at the render above — RmAuth's own bootstrap is
    // still in flight. ensureToken() resolves once it is (or resolves instantly
    // if some earlier call on the page already settled it), so the rewrite
    // lands as soon as it honestly can rather than guessing.
    if (window.RmAuth && RmAuth.ensureToken) {
        RmAuth.ensureToken().then(fixDashboardLinks);
    }
})(window);
