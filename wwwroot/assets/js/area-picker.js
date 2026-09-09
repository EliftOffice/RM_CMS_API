/*
 * Area picker — a text box that behaves like a dropdown.
 *
 * Shared by the visitor intake screen and the add-user screen, which is the whole
 * point: a volunteer and the visitor who lives two streets from them have to end
 * up on the SAME area row, or "who lives near this person" cannot be answered.
 * Two screens with two slightly different pickers is how that goes wrong.
 *
 *   GET /api/areas/options?q=&campusId=   the matches, active areas at one campus
 *
 * Written against plain DOM and fetch rather than jQuery, because the intake
 * screen does not load jQuery and this has to work on both screens unchanged.
 * auth.js intercepts window.fetch, so these calls are already authenticated.
 *
 * How it behaves, and why:
 *
 *   - It is an <input>, not a <select>. The operator types the area they were
 *     told; a dropdown would make them hunt for it, and would have no answer at
 *     all the first time a neighbourhood comes up.
 *
 *   - Matches appear as they type. Picking one fixes the id, so the same place
 *     typed three ways still lands on one row.
 *
 *   - Typing something that matches nothing is allowed. value() then returns the
 *     text with no id, and the SERVER creates the area and files the person
 *     against it in one request. The browser deliberately does not create it
 *     first: two operators typing the same new area at the same moment would
 *     race, and one of them would get a duplicate-key failure instead of a save.
 *
 *   - The id is dropped the instant the text stops matching what was picked. A
 *     stale id is the one genuinely dangerous state here — it would file someone
 *     against an area whose name is no longer on screen.
 *
 * Requires api-config.js and auth.js.
 */
(function (window, document) {
    'use strict';

    var DEBOUNCE_MS = 200;

    function escapeHtml(value) {
        return String(value == null ? '' : value)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function el(target) {
        return typeof target === 'string' ? document.getElementById(target) : target;
    }

    /**
     * @param {object} options
     *   input     {HTMLElement|string} the text box
     *   results   {HTMLElement|string} the container the matches render into
     *   campusId  {function():string}  optional — the campus to search, re-read on
     *                                  every keystroke so changing the campus
     *                                  picker changes the areas offered
     *   onChange  {function()}         optional — fired when the value changes
     */
    function attach(options) {
        var input = el(options.input);
        var results = el(options.results);

        if (!input || !results) return null;

        var chosen = null;        // { id, name } once something is picked
        var matches = [];
        var timer = null;
        var highlighted = -1;

        input.setAttribute('autocomplete', 'off');
        input.setAttribute('role', 'combobox');
        input.setAttribute('aria-autocomplete', 'list');
        input.setAttribute('aria-expanded', 'false');
        results.setAttribute('role', 'listbox');

        // ---------------------------------------------------------------- state

        function typed() { return input.value.trim(); }

        /**
         * The same rule the server uses to decide whether two names are the same
         * place: case and repeated spaces do not count. Keeping it identical here is
         * what lets the picker say "this is still the row you chose" after a stray
         * keystroke, instead of dropping the id over a capital letter.
         */
        function normalize(value) {
            return String(value == null ? '' : value).trim().replace(/\s+/g, ' ').toLowerCase();
        }

        function syncChoice() {
            if (chosen && normalize(chosen.name) !== normalize(typed())) chosen = null;
        }

        // ---------------------------------------------------------------- fetch

        function search() {
            var term = typed();
            var campus = options.campusId ? options.campusId() : '';

            var url = API_BASE_URL + '/areas/options?q=' + encodeURIComponent(term) +
                      (campus ? '&campusId=' + encodeURIComponent(campus) : '');

            fetch(url)
                .then(function (res) { return res.json(); })
                .then(function (body) {
                    // A refusal arrives as HTTP 200 with responseType 1. Treat
                    // anything but success as no matches rather than rendering
                    // `undefined`.
                    matches = (body && body.responseType === 0 && body.data) || [];
                    render(term);
                })
                .catch(function () {
                    // Non-fatal. The operator can still type a name and the server
                    // resolves it on save — losing the suggestions is not losing the
                    // ability to record an area.
                    close();
                });
        }

        // ---------------------------------------------------------------- render

        function render(term) {
            var exact = matches.some(function (m) { return normalize(m.name) === normalize(term); });

            var html = matches.map(function (m, i) {
                return '<div class="picker-item' + (i === highlighted ? ' picker-item-on' : '') + '"' +
                         ' role="option" data-i="' + i + '">' + escapeHtml(m.name) + '</div>';
            }).join('');

            // The "will be added" line answers the question the operator is actually
            // asking when nothing matches — "have I got this wrong, or is this place
            // simply not on the list yet?". Without it a blank dropdown reads as a
            // failure.
            if (term && !exact) {
                html += '<div class="picker-item picker-item-new' +
                          (highlighted === matches.length ? ' picker-item-on' : '') + '"' +
                        ' role="option" data-new="1">' +
                          'Add <strong>' + escapeHtml(term) + '</strong> as a new area' +
                        '</div>';
            }

            if (!html) { close(); return; }

            results.innerHTML = html;
            results.hidden = false;
            input.setAttribute('aria-expanded', 'true');
        }

        function close() {
            results.hidden = true;
            results.innerHTML = '';
            input.setAttribute('aria-expanded', 'false');
            highlighted = -1;
        }

        function choose(index) {
            // The last row is "add this as a new area" when it is offered. Choosing
            // it just closes the list: the text is already what it needs to be, and
            // the area itself is created by the server on save.
            if (index >= 0 && index < matches.length) {
                chosen = { id: matches[index].id, name: matches[index].name };
                input.value = matches[index].name;
            } else {
                chosen = null;
            }

            close();
            if (options.onChange) options.onChange();
        }

        function paintHighlight() {
            Array.prototype.forEach.call(results.children, function (child, i) {
                child.classList.toggle('picker-item-on', i === highlighted);
            });
        }

        // ---------------------------------------------------------------- events

        input.addEventListener('input', function () {
            syncChoice();
            highlighted = -1;

            clearTimeout(timer);
            timer = setTimeout(search, DEBOUNCE_MS);

            if (options.onChange) options.onChange();
        });

        input.addEventListener('focus', function () {
            clearTimeout(timer);
            timer = setTimeout(search, DEBOUNCE_MS);
        });

        input.addEventListener('keydown', function (e) {
            var count = results.hidden ? 0 : results.children.length;

            if (count === 0) {
                if (e.key === 'ArrowDown') search();
                return;
            }

            if (e.key === 'ArrowDown') {
                e.preventDefault();
                highlighted = (highlighted + 1) % count;
            } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                highlighted = (highlighted - 1 + count) % count;
            } else if (e.key === 'Enter') {
                // Only swallows Enter while a row is actually highlighted, so Enter
                // still submits the form the rest of the time.
                if (highlighted >= 0) { e.preventDefault(); choose(highlighted); }
                return;
            } else if (e.key === 'Escape') {
                close();
                return;
            } else {
                return;
            }

            paintHighlight();
        });

        // mousedown, not click: blur fires first on a click and would close the list
        // out from under the pointer.
        results.addEventListener('mousedown', function (e) {
            var item = e.target.closest('.picker-item');
            if (!item) return;

            e.preventDefault();

            var i = item.getAttribute('data-i');
            choose(i === null ? -1 : Number(i));
        });

        input.addEventListener('blur', function () {
            // Long enough for a mousedown on the list to land first.
            setTimeout(close, 150);
        });

        // ---------------------------------------------------------------- api

        return {
            /**
             * What to send. `id` is set only when a row was picked and the text still
             * matches it; otherwise the server resolves `name`, creating the area
             * when nothing matches.
             */
            value: function () {
                syncChoice();
                return { id: chosen ? chosen.id : null, name: typed() };
            },

            /** True when there is nothing to send. */
            isEmpty: function () { return typed().length === 0; },

            /** Pre-fills from an existing record. */
            set: function (id, name) {
                chosen = (id && name) ? { id: id, name: name } : null;
                input.value = name || '';
                close();
            },

            clear: function () {
                chosen = null;
                input.value = '';
                close();
            },

            input: input
        };
    }

    window.AreaPicker = { attach: attach };

})(window, document);
