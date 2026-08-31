/* ============================================================================
   Indian mobile number input.

   One implementation for every screen that takes a mobile number, because the
   number IS the username: a value accepted on one screen and rejected on
   another produces an account nobody can sign in to.

   The rule, in full:
     - exactly 10 digits
     - first digit 6, 7, 8 or 9   (the ranges TRAI allocates to mobile services;
                                   1-5 and 0 are landline, service and routing
                                   prefixes and can never be a mobile)

   Deliberately NOT accepted: +91 prefixes, spaces, hyphens and brackets. The
   stored value is the username, so one person typing '+91 98765 43210' and
   another typing '9876543210' would be two different logins for one human.
   Rather than reject those, the field strips them as they are typed — the
   operator reading a number off a card should not have to retype it.

   No jQuery: people-entry.js is plain DOM and add-user.js is jQuery, and this
   has to work in both.
   ========================================================================== */
(function (window, document) {
    'use strict';

    var LENGTH = 10;
    var VALID = /^[6-9][0-9]{9}$/;

    /**
     * Digits only, capped at ten, with dialling prefixes removed FIRST.
     *
     * The order matters and is the whole point of this function. Stripping
     * punctuation and then truncating turns '+91 98765 43210' into '9198765432'
     * — ten digits, starting with 9, so it passes every check and is somebody
     * else's number entirely. Country and trunk prefixes have to come off before
     * the length is judged.
     *
     * Only stripped when what remains is still too long, because '9198765432' is
     * itself a perfectly good number for someone whose phone starts 91.
     */
    function clean(value) {
        var digits = String(value == null ? '' : value).replace(/\D/g, '');

        // 00 91 98765 43210 -> 91 98765 43210 -> 9876543210
        if (digits.length > LENGTH && digits.indexOf('00') === 0) digits = digits.slice(2);
        if (digits.length > LENGTH && digits.indexOf('91') === 0) digits = digits.slice(2);

        // 0 98765 43210 — the domestic trunk prefix.
        if (digits.length > LENGTH && digits.charAt(0) === '0') digits = digits.slice(1);

        return digits.slice(0, LENGTH);
    }

    function isValid(value) {
        return VALID.test(clean(value));
    }

    /**
     * Why this value is not usable yet, or null when it is.
     *
     * Ordered so the message answers what the operator is most likely doing:
     * mid-typing gets a count, a finished-but-wrong number gets the rule.
     */
    function problem(value) {
        var digits = clean(value);

        if (!digits.length) return null;          // empty is the form's business

        if (digits.length < LENGTH) {
            var missing = LENGTH - digits.length;
            return missing === 1
                ? '1 more digit needed.'
                : missing + ' more digits needed.';
        }

        if (!VALID.test(digits))
            return 'An Indian mobile number starts with 6, 7, 8 or 9.';

        return null;
    }

    /**
     * Binds the rules to an input.
     *
     * @param {HTMLInputElement} input
     * @param {object} [options]
     *        hint     — element to write the message into
     *        hintText — what the hint says when the value is fine
     *        onState  — called with (isValid, digits) after every change
     */
    function attach(input, options) {
        if (!input || input.dataset.mobileBound === '1') return;

        options = options || {};

        var hint = options.hint || null;
        var restText = options.hintText || (hint ? hint.textContent : '');

        input.setAttribute('type', 'tel');
        input.setAttribute('inputmode', 'numeric');
        input.setAttribute('autocomplete', 'tel');
        input.setAttribute('maxlength', String(LENGTH));

        function apply() {
            var before = input.value;
            var digits = clean(before);

            // Only touch the field when something was actually removed, so the
            // caret does not jump on every keystroke of ordinary typing.
            if (before !== digits) {
                var atEnd = input.selectionStart === before.length;
                input.value = digits;

                if (!atEnd) {
                    try {
                        var pos = Math.min(input.selectionStart, digits.length);
                        input.setSelectionRange(pos, pos);
                    } catch (e) { /* some input types refuse setSelectionRange */ }
                }
            }

            var message = problem(digits);
            var ok = VALID.test(digits);

            if (hint) {
                hint.textContent = message || restText;
                hint.classList.toggle('hint-bad', !!message);
            }

            // Keeps the browser's own validity in step, so a native form submit
            // cannot slip past the rule.
            input.setCustomValidity(
                digits.length && !ok ? (message || 'Enter a valid mobile number.') : '');

            if (options.onState) options.onState(ok, digits);

            return ok;
        }

        // keyup is the event asked for. 'input' is bound as well because keyup
        // never fires for a right-click paste, a drag-drop, or most Android
        // autofill — and a rule that a paste walks straight through is not a
        // rule. 'blur' catches anything both of them miss.
        input.addEventListener('keyup', apply);
        input.addEventListener('input', apply);
        input.addEventListener('blur', apply);

        input.dataset.mobileBound = '1';

        return { validate: apply };
    }

    window.MobileInput = {
        LENGTH: LENGTH,
        attach: attach,
        clean: clean,
        isValid: isValid,
        problem: problem
    };
})(window, document);
