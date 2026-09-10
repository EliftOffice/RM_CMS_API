using System.Text.RegularExpressions;

namespace RM_CMS.Modules.MessageTemplates.Domain
{
    /// <summary>
    /// Substitutes <c>{{Token}}</c> values into a template body.
    /// </summary>
    /// <remarks>
    /// THE ESCAPING RULE, WHICH IS THE WHOLE POINT OF THIS CLASS:
    /// messages are sent with <c>parse_mode=HTML</c>, so the TEMPLATE is markup — a
    /// pastor may legitimately write <c>&lt;b&gt;</c> in it and expect bold. The VALUES
    /// are not markup. They are names, reasons and localities typed by staff or given
    /// by visitors, and a name containing an ampersand or an angle bracket would make
    /// Telegram reject the entire message rather than render it plainly.
    ///
    /// So the template passes through untouched and every substituted value is escaped.
    /// Getting this backwards in either direction breaks something: escaping the
    /// template makes the formatting show up as literal tags, and not escaping the
    /// values loses real messages to a person called "A &amp; B".
    ///
    /// A value that is itself meant to be markup — the escalation heading, or the
    /// team-lead line that is either a formatted sentence or nothing — is passed
    /// through <see cref="RawValues"/> instead, which is deliberately awkward to use.
    /// </remarks>
    public static partial class TemplateRenderer
    {
        /// <summary>
        /// Telegram refuses a message over 4096 characters. Templates are checked
        /// against this when saved, but a template well under it can still render long
        /// once names and reasons are substituted, so the result is capped too.
        /// </summary>
        public const int MaxMessageLength = 4096;

        [GeneratedRegex(@"\{\{\s*([A-Za-z][A-Za-z0-9_]*)\s*\}\}")]
        private static partial Regex TokenPattern();

        /// <summary>
        /// Renders <paramref name="body"/>, escaping every value in
        /// <paramref name="values"/> and passing anything in <paramref name="rawValues"/>
        /// through as markup.
        /// </summary>
        /// <remarks>
        /// A token with no value becomes an empty string rather than being left on
        /// screen. Save-time validation already refuses unknown tokens, so reaching
        /// this means the catalogue changed under a template that was saved earlier —
        /// and a blank reads better to a volunteer than a stray <c>{{Whatever}}</c>.
        /// </remarks>
        public static string Render(
            string body,
            IReadOnlyDictionary<string, string?> values,
            IReadOnlyDictionary<string, string?>? rawValues = null)
        {
            var rendered = TokenPattern().Replace(body, match =>
            {
                var name = match.Groups[1].Value;

                if (rawValues is not null && rawValues.TryGetValue(name, out var raw))
                    return raw ?? string.Empty;

                return values.TryGetValue(name, out var value)
                    ? Escape(value ?? string.Empty)
                    : string.Empty;
            });

            // Blank lines pile up wherever an empty token sat alone on its own line —
            // the team-lead and safeguarding notes do exactly that — and three blank
            // lines in a row look like the message was cut off.
            rendered = CollapseBlankLines(rendered).Trim();

            return rendered.Length <= MaxMessageLength
                ? rendered
                : rendered[..MaxMessageLength];
        }

        /// <summary>
        /// The tokens this body uses, whatever they are. Used to tell an editor which
        /// of them this scenario does not recognise.
        /// </summary>
        public static IReadOnlyList<string> TokensIn(string body) =>
            TokenPattern().Matches(body)
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();

        /// <summary>
        /// Escapes a value for Telegram's HTML parse mode.
        ///
        /// Only these three characters are special, and over-escaping would put
        /// <c>&amp;#39;</c> in front of somebody where an apostrophe belongs.
        /// </summary>
        public static string Escape(string value) =>
            value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        [GeneratedRegex(@"\n{3,}")]
        private static partial Regex ExcessBlankLines();

        private static string CollapseBlankLines(string value) =>
            ExcessBlankLines().Replace(value.Replace("\r\n", "\n"), "\n\n");
    }
}
