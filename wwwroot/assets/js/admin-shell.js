/*
 * Shared admin chrome: navigation, the signed-in badge, and sign-out.
 *
 * The MVP's admin pages were three disconnected islands with no common
 * navigation. This renders one header for every admin screen, so adding a page
 * means adding a line to NAV rather than hand-editing markup in each file.
 *
 * Requires auth.js (for RmAuth) and toast.js.
 */
(function (window) {
    'use strict';

    // Root-relative, not page-relative: these links are rendered into pages in
    // different folders, and 'accounts.html' resolves against whichever folder the
    // current page happens to live in.
    var NAV = [
        {
            label: 'Record a visitor',
            href: '/pages/intake/record-visitor.html',
            roles: ['ADMIN', 'PASTOR', 'TEAM_LEAD', 'VOLUNTEER', 'DATA_ENTRY']
        },
        // 'Add volunteer' used to sit here. It has been folded into Add a user,
        // which does the same enrolment plus the sign-in decision in one pass —
        // two screens that both created volunteers was the source of volunteers
        // enrolled without a login, who can never be assigned anything.
        { label: 'Users', href: '/pages/admin/users.html', roles: ['ADMIN'] },
        {
            // Pastors and team leads are listed because an administrator CAN grant
            // them this screen (team.manage_by_pastor / team.manage_by_team_lead).
            // The link showing is not the grant: the page asks /api/teams/access on
            // load and replaces itself with an explanation when nothing is granted.
            // Hiding it until granted would need the nav to read settings on every
            // page, and a link that explains itself beats one that is simply absent.
            label: 'Teams',
            href: '/pages/admin/teams.html',
            roles: ['ADMIN', 'PASTOR', 'TEAM_LEAD']
        },
        {
            // Administrators only, with no grant to widen it. Creating a campus
            // creates a tenancy boundary, and retiring one decides whose records
            // stop being reachable — neither is a pastor's call.
            label: 'Campuses',
            href: '/pages/admin/campuses.html',
            roles: ['ADMIN']
        },
        {
            // Data entry operators are listed because an administrator CAN grant
            // them this screen (area.manage_by_data_entry) — they are the people
            // typing area names all day, so they see a misspelling first. As with
            // Teams, the link showing is not the grant: the page asks
            // /api/areas/access on load and replaces itself with an explanation
            // when nothing is granted.
            label: 'Areas',
            href: '/pages/admin/areas.html',
            roles: ['ADMIN', 'DATA_ENTRY']
        },
        {
            // The website coordinator's only screen, and an administrator's view of
            // what the public site is collecting. Nothing else in the nav renders for
            // a WEB_COORDINATOR, which is correct: everything else 403s for them.
            label: 'Website',
            href: '/pages/admin/web-enquiries.html',
            roles: ['ADMIN', 'WEB_COORDINATOR']
        },
        {
            // The church calendar the public website lists. Pastors get it because the
            // calendar is theirs; the coordinator because publishing to the public site
            // is what that role is for.
            label: 'Events',
            href: '/pages/admin/events.html',
            roles: ['ADMIN', 'PASTOR', 'WEB_COORDINATOR']
        },
        { label: 'Accounts', href: '/pages/admin/accounts.html', roles: ['ADMIN'] },
        { label: 'Settings', href: '/pages/admin/settings.html', roles: ['ADMIN'] },
        { label: 'Telegram', href: '/pages/admin/telegram.html', roles: ['ADMIN'] },
        {
            // The wording of every Telegram message. Separate from the Telegram setup
            // screen next door, which is about the bot connection rather than what it
            // says — one is plumbing, the other is pastoral tone.
            label: 'Messages',
            href: '/pages/admin/telegram-messages.html',
            roles: ['ADMIN']
        }
    ];

    function escapeHtml(value) {
        return String(value == null ? '' : value)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    /**
     * Renders the header into #adminShell.
     *
     * `active` is either the href of the current page, or { href, area } where
     * `area` overrides the brand sub-label. The string form is kept because the
     * existing admin pages pass it.
     */
    function render(active) {
        var host = document.getElementById('adminShell');
        if (!host) return;

        var options = (active && typeof active === 'object') ? active : { href: active };

        var account = RmAuth.getUser() || {};
        var roles = RmAuth.roleCodes();

        var links = NAV
            .filter(function (item) {
                return !item.roles || item.roles.some(function (r) { return roles.indexOf(r) !== -1; });
            })
            .map(function (item) {
                var cls = (item.href === options.href) ? 'shell-link shell-link-active' : 'shell-link';
                return '<a class="' + cls + '" href="' + escapeHtml(item.href) + '">' + escapeHtml(item.label) + '</a>';
            })
            .join('');

        // The sub-label names the area, not the role. An intake operator is not in
        // an "Admin" console and should not be told they are.
        var area = options.area || 'Admin';

        host.innerHTML =
            '<header class="shell">' +
              '<div class="shell-brand">RM_CMS <span class="shell-brand-sub">' +
                escapeHtml(area) + '</span></div>' +
              '<nav class="shell-nav">' + links + '</nav>' +
              '<div class="shell-user">' +
                '<span class="shell-name">' + escapeHtml(RmAuth.pick(account, 'fullName') || '') + '</span>' +
                '<span class="shell-role">' + escapeHtml(roles.join(' · ')) + '</span>' +
                '<button type="button" id="shellSignOut" class="shell-signout">Sign out</button>' +
              '</div>' +
            '</header>';

        document.getElementById('shellSignOut').addEventListener('click', function () {
            RmAuth.logout();
        });
    }

    /**
     * Boots a protected admin page: establishes the session, enforces the role,
     * renders the header, then hands control back.
     */
    function boot(options) {
        options = options || {};

        return RmAuth.bootstrap({ roles: options.roles || ['ADMIN'] }).then(function (ok) {
            if (!ok) return false;      // bootstrap has already redirected
            render(options.active);
            return true;
        });
    }

    window.AdminShell = { boot: boot, render: render, escapeHtml: escapeHtml };

})(window);
