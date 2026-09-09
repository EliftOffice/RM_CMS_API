/*
 * Telegram setup — administrator diagnostics and the one-time webhook registration.
 *
 * Deliberately NOT a form for the bot token. The token lets whoever holds it send
 * messages as the church, so it lives in an environment variable: typing it into a web
 * form would put it in a request body, the server log, and browser history. This screen
 * reports whether each secret is PRESENT and never what it is.
 *
 *   GET  /api/telegram/setup             what is configured, and what Telegram thinks
 *   POST /api/telegram/webhook/register  point Telegram at this deployment
 */
$(function () {
    'use strict';

    var esc = AdminShell.escapeHtml;

    AdminShell.boot({
        roles: ['ADMIN'],
        active: { href: '/pages/admin/telegram.html', area: 'Admin' }
    }).then(function (ok) {
        if (!ok) return;
        $('#pageBody').prop('hidden', false);
        load();

        $('#refreshBtn').on('click', load);
        $('#registerBtn').on('click', register);
        $('#testBtn').on('click', load);
        $('#copyEnvBtn').on('click', copyEnv);
    });

    function load() {
        $('#checks').html('<p class="empty">Checking…</p>');

        $.ajax({ url: API_BASE_URL + '/telegram/setup', method: 'GET' })
            .done(function (res) {
                var s = (res && res.data) || {};
                render(s);
            })
            .fail(function (xhr) {
                $('#checks').html('<p class="empty">Could not read the Telegram status.</p>');
                showToast(xhr.status === 403
                    ? 'Only administrators can view Telegram setup.'
                    : 'Could not read the Telegram status.', 'error');
            });
    }

    function render(s) {
        var rows = [];

        rows.push(check(
            s.botTokenConfigured,
            'Bot token',
            s.botTokenConfigured
                ? 'Present. Its value is never shown here.'
                : 'Missing — set TELEGRAM__BOTTOKEN and restart.'));

        rows.push(check(
            !!s.botUsername,
            'Bot username',
            s.botUsername
                ? esc(s.botUsername) + ' — used to build the link people open.'
                : 'Missing — set TELEGRAM__BOTUSERNAME and restart.'));

        rows.push(check(
            s.webhookSecretConfigured,
            'Webhook secret',
            s.webhookSecretConfigured
                ? 'Present. Telegram must send it with every delivery.'
                : 'Missing — without it the webhook refuses everything, including Telegram.'));

        // Only meaningful once a token exists; otherwise it is noise.
        if (s.botTokenConfigured) {
            rows.push(check(
                s.connectionOk,
                'Connection to Telegram',
                s.connectionOk
                    ? 'Telegram recognises this bot as ' + esc(s.connectionDetail || '') + '.'
                    : esc(s.connectionDetail || 'Telegram did not accept the token.')));

            rows.push(check(
                s.webhookRegistered,
                'Webhook',
                s.webhookRegistered
                    ? 'Delivering to ' + esc(s.webhookUrl || '')
                    : 'Not registered — Telegram has nowhere to deliver messages.'));
        }

        $('#checks').html(rows.join(''));
        $('#linkedCount').text(s.linkedPeople == null ? '—' : s.linkedPeople);

        // Registering needs a working token and a secret to register WITH.
        var canRegister = s.botTokenConfigured && s.webhookSecretConfigured;

        $('#registerBtn').prop('disabled', !canRegister);
        $('#testBtn').prop('disabled', !s.botTokenConfigured);

        var notes = [];

        if (s.pendingUpdates > 0) {
            notes.push(s.pendingUpdates + ' update(s) are queued at Telegram, which usually ' +
                       'means deliveries are failing.');
        }

        if (s.lastError) {
            // Telegram keeps reporting the last failure long after it is fixed.
            notes.push('Telegram last reported: "' + s.lastError + '". This is history — ' +
                       'it stays until a delivery succeeds.');
        }

        if (!s.webhookRegistered && canRegister) {
            notes.push('Registering will point Telegram at ' + (s.suggestedWebhookUrl || 'this server') +
                       '. That address must be reachable from the internet over HTTPS.');
        }

        $('#webhookNote').text(notes.join(' '));
    }

    function check(ok, title, detail) {
        return '<div class="check-row">' +
                 '<div class="check-mark ' + (ok ? 'check-ok' : 'check-bad') + '">' +
                   (ok ? '&#10003;' : '&#10007;') + '</div>' +
                 '<div>' +
                   '<div class="check-title">' + esc(title) + '</div>' +
                   '<div class="check-sub">' + detail + '</div>' +
                 '</div>' +
               '</div>';
    }

    function register() {
        var $btn = $('#registerBtn').prop('disabled', true).text('Registering…');

        $.ajax({ url: API_BASE_URL + '/telegram/webhook/register', method: 'POST' })
            .done(function (res) {
                $btn.text('Register webhook');

                showToast((res && res.message) || 'Done',
                          res && res.responseType === 0 ? 'success' : 'warning');

                load();
            })
            .fail(function () {
                $btn.prop('disabled', false).text('Register webhook');
                showToast('Could not reach Telegram to register the webhook.', 'error');
            });
    }

    function copyEnv() {
        // Copies the variable names and placeholders, never a real value — there is
        // no real value on this page to copy.
        var text = $('#envBlock').text();

        if (navigator.clipboard) {
            navigator.clipboard.writeText(text).then(function () {
                showToast('Copied. Paste into your deployment environment.', 'success');
            });
        } else {
            showToast('Copy is not available in this browser.', 'warning');
        }
    }
});
