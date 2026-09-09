/*
 * Connect Telegram — the sibling of the change-password screen in the post-sign-in flow.
 *
 * Shown after sign-in when the person has no verified TELEGRAM contact. Whether it can
 * be skipped is an administrator setting (telegram.require_linking), not a decision
 * baked into this page — turning it on before the bot is configured would otherwise
 * lock everyone out, so the server reports both "is it required" and "is it available"
 * and this page honours the combination.
 *
 *   GET  /api/telegram/status      linked? required? configured?
 *   POST /api/telegram/invitation  mints a one-time deep link
 *
 * The link carries a random token, never the person id, so it cannot be reversed into
 * a database key or reused for somebody else.
 */
$(function () {
    'use strict';

    var POLL_MS = 2500;
    var POLL_LIMIT = 240;   // ~10 minutes, then stop asking

    var polls = 0;
    var timer = null;
    var status = null;

    RmAuth.bootstrap({ roles: [] }).then(function (ok) {
        if (!ok) return;    // bootstrap already redirected to sign-in
        load();
        wire();
    });

    function wire() {
        $('#connectBtn').on('click', connect);
        $('#skipBtn').on('click', leave);
        $('#continueBtn').on('click', leave);

        // Coming back from the Telegram app fires this, so the page checks
        // immediately rather than waiting for the next poll tick.
        document.addEventListener('visibilitychange', function () {
            if (!document.hidden && timer) check();
        });
    }

    function load() {
        $.ajax({ url: API_BASE_URL + '/telegram/status', method: 'GET' })
            .done(function (res) {
                status = (res && res.data) || {};

                if (status.isLinked) return showConnected(status.username);

                if (!status.isConfigured) {
                    $('#startBlock').hide();
                    $('#unavailableBlock').show();
                }

                // Skip is offered unless an administrator made linking mandatory.
                // Even then it stays available when the bot is unconfigured, so a
                // misconfiguration cannot strand people on this screen.
                $('#skipBtn').toggle(!status.isRequired || !status.isConfigured);

                if (status.isRequired && status.isConfigured) {
                    $('#intro').text(
                        'Your organisation requires Telegram to be connected before you ' +
                        'can use RM CMS. It only needs doing once.');
                }
            })
            .fail(function () {
                showToast('Could not check your Telegram status.', 'error');
                $('#skipBtn').show();
            });
    }

    function connect() {
        var $btn = $('#connectBtn').prop('disabled', true).text('Preparing…');

        $.ajax({ url: API_BASE_URL + '/telegram/invitation', method: 'POST', contentType: 'application/json', data: '{}' })
            .done(function (res) {
                $btn.prop('disabled', false).text('Connect Telegram');

                if (!res || res.responseType !== 0 || !res.data) {
                    showToast((res && res.message) || 'Could not create the link.', 'warning');
                    return;
                }

                $('#openTelegramBtn').attr('href', res.data.deepLink);
                $('#startBlock').hide();
                $('#linkBlock').show();

                if (res.data.expiresAt) {
                    var expires = new Date(res.data.expiresAt);
                    $('#expiryHint').text('This link stops working at ' +
                        expires.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) + '.');
                }

                startPolling();
            })
            .fail(function () {
                $btn.prop('disabled', false).text('Connect Telegram');
                showToast('Could not create the link. Please try again.', 'error');
            });
    }

    function startPolling() {
        stopPolling();
        polls = 0;
        timer = setInterval(check, POLL_MS);
    }

    function stopPolling() {
        if (timer) { clearInterval(timer); timer = null; }
    }

    /** Asks whether the webhook has linked this person yet. */
    function check() {
        if (++polls > POLL_LIMIT) {
            stopPolling();
            $('#waiting').removeClass('tg-waiting').addClass('tg-warn')
                .html('<span>Still not connected. Generate a new link and try again.</span>');
            $('#startBlock').show();
            return;
        }

        $.ajax({ url: API_BASE_URL + '/telegram/status', method: 'GET' })
            .done(function (res) {
                var data = (res && res.data) || {};
                if (data.isLinked) showConnected(data.username);
            });
    }

    function showConnected(username) {
        stopPolling();

        $('#startBlock, #linkBlock, #unavailableBlock, #skipBtn').hide();
        $('#doneText').text(username ? 'Connected as ' + username + '.' : 'Connected.');
        $('#doneBlock').show();

        // The gate is cleared, so the next page load will not send them back here.
        setTimeout(leave, 1500);
    }

    /**
     * Hands control to the normal landing page for this account's role. Reuses the
     * sign-in screen's routing rather than repeating the role-to-page mapping.
     */
    function leave() {
        stopPolling();

        var target = sessionStorage.getItem('rm_post_login_target');
        sessionStorage.removeItem('rm_post_login_target');

        window.location.href = target || '/pages/auth/login.html';
    }
});
