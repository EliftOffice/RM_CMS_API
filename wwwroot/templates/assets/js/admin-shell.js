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

    var NAV = [
        { label: 'Accounts',   href: 'accounts.html',   roles: ['ADMIN'] },
        { label: 'Settings',   href: 'siteadmin.html',  roles: ['ADMIN'] }
    ];

    function escapeHtml(value) {
        return String(value == null ? '' : value)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    /**
     * Renders the header into #adminShell.
     * `active` is the href of the current page so it can be marked.
     */
    function render(active) {
        var host = document.getElementById('adminShell');
        if (!host) return;

        var account = RmAuth.getUser() || {};
        var roles = RmAuth.roleCodes();

        var links = NAV
            .filter(function (item) {
                return !item.roles || item.roles.some(function (r) { return roles.indexOf(r) !== -1; });
            })
            .map(function (item) {
                var cls = (item.href === active) ? 'shell-link shell-link-active' : 'shell-link';
                return '<a class="' + cls + '" href="' + escapeHtml(item.href) + '">' + escapeHtml(item.label) + '</a>';
            })
            .join('');

        host.innerHTML =
            '<header class="shell">' +
              '<div class="shell-brand">RM_CMS <span class="shell-brand-sub">Admin</span></div>' +
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
