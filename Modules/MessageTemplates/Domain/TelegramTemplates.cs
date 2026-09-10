namespace RM_CMS.Modules.MessageTemplates.Domain
{
    /// <summary>
    /// One <c>{{Token}}</c> a template may use, and what it stands for.
    /// </summary>
    /// <remarks>
    /// The description is written for a pastor, not a developer — it is shown beside
    /// the editor and is the only documentation this feature has.
    /// </remarks>
    public sealed record TemplatePlaceholder(string Token, string Description, string Sample);

    /// <summary>
    /// One scenario the church can reword, with its default text.
    /// </summary>
    public sealed record TelegramTemplateDefinition(
        string Code,
        string Label,
        string Description,
        string Group,
        IReadOnlyList<TemplatePlaceholder> Placeholders,
        string DefaultBody)
    {
        /// <summary>
        /// True when this message answers somebody who just did something in Telegram,
        /// as opposed to being sent out on the church's own initiative.
        /// </summary>
        /// <remarks>
        /// It matters for one reason: an interactive reply must always say something.
        /// Silence after tapping a link reads as broken, so these are never suppressed.
        /// </remarks>
        public bool IsReply => Group == TemplateGroups.Linking;
    }

    public static class TemplateGroups
    {
        public const string Linking = "Connecting Telegram";
        public const string Care = "Care and assignments";
        public const string Escalations = "Escalations";
        public const string Teams = "Teams";
    }

    /// <summary>
    /// Every Telegram message this system sends, and the wording it uses when nobody
    /// has changed it.
    /// </summary>
    /// <remarks>
    /// THIS FILE IS THE DEFAULT, NOT THE SOURCE OF TRUTH AT RUNTIME. A row in
    /// <c>telegram_template</c> overrides the body here; no row means this text is
    /// used. That is what lets a new scenario ship working, and what makes "reset to
    /// default" a delete rather than a second copy of the wording.
    ///
    /// ON THE TELUGU IN THE DEFAULTS: the existing messages are written in Telugu with
    /// English headings, because that is what the congregation reads. The defaults keep
    /// exactly the wording that was in the code before this screen existed, so turning
    /// this feature on changes nothing until somebody chooses to change it.
    ///
    /// ADDING A SCENARIO: add it here, add a branch where the message is sent, and it
    /// appears on the admin screen by itself. Nothing needs seeding and no migration is
    /// involved.
    /// </remarks>
    public static class TelegramTemplates
    {
        // ==================================================================
        // Scenario codes
        //
        // The Care and Escalation codes deliberately match NotificationType, so a
        // queued alert finds its wording by the same string it was queued under.
        // ==================================================================

        /// <summary>Somebody finished connecting their Telegram account.</summary>
        public const string LinkWelcome = "LINK_WELCOME";

        /// <summary>They tapped /start again on a chat that is already connected.</summary>
        public const string LinkAlreadyConnected = "LINK_ALREADY_CONNECTED";

        /// <summary>A /start with no token from a chat nobody recognises.</summary>
        public const string LinkUnknownChat = "LINK_UNKNOWN_CHAT";

        /// <summary>The personal link had expired or had already been used.</summary>
        public const string LinkExpired = "LINK_EXPIRED";

        /// <summary>This Telegram account already belongs to a different person.</summary>
        public const string LinkClaimedByAnother = "LINK_CLAIMED_BY_ANOTHER";

        /// <summary>The administrator's "did this arrive?" message.</summary>
        public const string TestMessage = "TEST_MESSAGE";

        /// <summary>
        /// "Is this you signing in?", sent with Confirm and Refuse buttons underneath.
        /// </summary>
        public const string LoginVerification = "LOGIN_VERIFICATION";

        /// <summary>A case was assigned to a volunteer.</summary>
        public const string CaseAssigned = "CASE_ASSIGNED";

        public const string EscalationRaised = "ESCALATION_RAISED";
        public const string EscalationUnacknowledged = "ESCALATION_UNACKNOWLEDGED";
        public const string EscalationPastorAlert = "ESCALATION_PASTOR_ALERT";
        public const string HuddleReminder = "HUDDLE_REMINDER";

        // ==================================================================
        // Placeholders
        // ==================================================================

        /// <summary>
        /// Available in every template. These are the "user details of the receiver"
        /// that make a message personal — the person it is being sent to, not the
        /// person it is about.
        /// </summary>
        public static readonly IReadOnlyList<TemplatePlaceholder> Common = new List<TemplatePlaceholder>
        {
            new("{{RecipientName}}", "The full name of the person receiving this message", "Ravi Kumar"),
            new("{{RecipientFirstName}}", "Their first name only — friendlier in a greeting", "Ravi"),
            new("{{ChurchName}}", "The church's name", "Resurrection Ministries"),
            new("{{SiteUrl}}", "Web address of this system, for a link they can tap", "https://rmoffice.online")
        };

        private static readonly TemplatePlaceholder PersonName =
            new("{{PersonName}}", "The person the message is ABOUT, who is not the recipient", "Lakshmi Devi");

        private static readonly TemplatePlaceholder Reference =
            new("{{Reference}}", "The reference code staff quote to each other", "V014");

        private static readonly TemplatePlaceholder MyAssignmentsLink =
            new("{{MyAssignmentsLink}}", "Direct link to their own follow-up list", "https://rmoffice.online/pages/care/my-assignments.html");

        // ==================================================================
        // The catalogue
        // ==================================================================

        public static readonly IReadOnlyList<TelegramTemplateDefinition> All =
            new List<TelegramTemplateDefinition>
            {
                // ---- Connecting Telegram ----------------------------------
                new(LinkWelcome,
                    "Welcome after connecting",
                    "Sent the moment somebody finishes connecting their Telegram account. " +
                    "The first thing this system ever says to them.",
                    TemplateGroups.Linking,
                    new List<TemplatePlaceholder>
                    {
                        new("{{TelegramName}}", "The name shown on their Telegram account", "Ravi")
                    },
                    "✅ Connected. Thank you, {{TelegramName}} — {{ChurchName}} will now reach you here."),

                new(LinkAlreadyConnected,
                    "Already connected",
                    "They tapped the link again on a chat that is already connected. Reassuring, not an error.",
                    TemplateGroups.Linking,
                    new List<TemplatePlaceholder>
                    {
                        new("{{TelegramName}}", "The name shown on their Telegram account", "Ravi")
                    },
                    "You are already connected to {{ChurchName}}, {{TelegramName}}."),

                new(LinkUnknownChat,
                    "Chat we do not recognise",
                    "Somebody started the bot without a personal link. Tells them where to get one.",
                    TemplateGroups.Linking,
                    Array.Empty<TemplatePlaceholder>(),
                    "Hello. To connect this Telegram account, open the personal link from your " +
                    "{{ChurchName}} profile — the Connect Telegram button on your account page."),

                new(LinkExpired,
                    "Link expired or already used",
                    "Their personal link no longer works. A link is single-use and short-lived on purpose.",
                    TemplateGroups.Linking,
                    Array.Empty<TemplatePlaceholder>(),
                    "That link has expired or has already been used. " +
                    "Please generate a new one from your {{ChurchName}} profile."),

                new(LinkClaimedByAnother,
                    "Already belongs to someone else",
                    "This Telegram account is connected to a different profile. Never reassigned " +
                    "automatically — being wrong here sends one person's pastoral alerts to another phone.",
                    TemplateGroups.Linking,
                    Array.Empty<TemplatePlaceholder>(),
                    "This Telegram account is already connected to a different {{ChurchName}} profile. " +
                    "Please contact an administrator."),

                new(TestMessage,
                    "Test message",
                    "Sent by an administrator from the Telegram screen to check a connection works.",
                    TemplateGroups.Linking,
                    Array.Empty<TemplatePlaceholder>(),
                    "This is a test message from {{ChurchName}}. If you did not expect it, " +
                    "please tell the church office — it may have been sent to the wrong person."),

                new(LoginVerification,
                    "Confirm a sign-in",
                    "Sent when somebody signs in and the church has sign-in confirmation switched on. " +
                    "Two buttons are added underneath it automatically — you cannot change those, " +
                    "only the words above them.",
                    TemplateGroups.Linking,
                    new List<TemplatePlaceholder>
                    {
                        new("{{Device}}", "The kind of device the sign-in came from, in plain words", "an Android phone"),
                        new("{{Minutes}}", "How long they have to tap before it stops working", "3")
                    },
                    @"🔐 Sign-in confirmation

🙏 Praise the Lord, {{RecipientFirstName}},

Somebody is signing in to {{ChurchName}} as you, from {{Device}}.

<b>If this is you</b>, tap the green button below.
<b>If it is not</b>, tap the red one and tell the church office — somebody else knows your password.

This expires in {{Minutes}} minutes."),

                // ---- Care -------------------------------------------------
                new(CaseAssigned,
                    "A follow-up was assigned to a volunteer",
                    "Sent to the volunteer when a case is placed with them, so they learn about it " +
                    "without having to check the screen.",
                    TemplateGroups.Care,
                    new List<TemplatePlaceholder>
                    {
                        PersonName,
                        Reference,
                        // Offered but deliberately NOT in the default wording below.
                        // Putting somebody's phone number into a Telegram message is a
                        // decision the church should make on purpose, not inherit from
                        // a default — Telegram history outlives the follow-up.
                        new("{{PersonPhone}}", "Their phone number. Think before adding this: it puts a number into Telegram history permanently", "9876543210"),
                        new("{{CampusName}}", "The campus the case belongs to", "Ongole"),
                        new("{{TeamName}}", "The team it sits under", "Ongole Team A"),
                        MyAssignmentsLink
                    },
                    @"🙋 కొత్త Follow-up

🙏 Praise the Lord, {{RecipientFirstName}},

<b>{{PersonName}}</b> గారి follow-up మీకు ఇవ్వబడింది.

Ref: <code>{{Reference}}</code>

దయచేసి వీలైనంత త్వరగా సంప్రదించండి. వివరాలు మీ list లో ఉన్నాయి. 🙏

👉 {{MyAssignmentsLink}}"),

                // ---- Escalations ------------------------------------------
                new(EscalationRaised,
                    "Escalation raised",
                    "Sent when an escalation is first raised.",
                    TemplateGroups.Escalations,
                    EscalationPlaceholders(),
                    EscalationDefault(pastor: false)),

                new(EscalationUnacknowledged,
                    "Escalation still not acknowledged",
                    "The chase-up to the assigned team lead when nobody has picked an escalation up.",
                    TemplateGroups.Escalations,
                    EscalationPlaceholders(),
                    EscalationDefault(pastor: false)),

                new(EscalationPastorAlert,
                    "Pastor alert",
                    "The escalated chase-up, once an escalation has waited past the pastor threshold.",
                    TemplateGroups.Escalations,
                    EscalationPlaceholders(),
                    EscalationDefault(pastor: true)),

                // ---- Teams ------------------------------------------------
                new(HuddleReminder,
                    "Weekly huddle reminder",
                    "Sent to a team lead and their team on huddle day. Carries NO pastoral detail " +
                    "on purpose — it goes to a whole team, and names discussed at the huddle are " +
                    "exactly what should not be broadcast to a group chat.",
                    TemplateGroups.Teams,
                    new List<TemplatePlaceholder>
                    {
                        new("{{TeamName}}", "The team the huddle belongs to", "Ongole Team A"),
                        new("{{TeamLeadLink}}", "Link to the team lead dashboard", "https://rmoffice.online/pages/dashboard/team-lead.html")
                    },
                    @"🤝 Team Huddle — ఈ రోజు

🙏 Praise the Lord,

ఈ రోజు మన <b>Team Huddle</b> ఉంది. ఈ వారం చేసిన follow-ups గురించి కలిసి మాట్లాడుకుందాం.

దయచేసి సమయానికి రండి. 🙏

👉 {{TeamLeadLink}}")
            };

        private static IReadOnlyList<TemplatePlaceholder> EscalationPlaceholders() =>
            new List<TemplatePlaceholder>
            {
                new("{{Title}}", "The heading the system chooses from the tier — emergency, pastor alert or ordinary", "🚨 EMERGENCY — వెంటనే స్పందించండి"),
                PersonName,
                new("{{Reason}}", "Why it was escalated", "No contact after three attempts"),
                new("{{Tier}}", "How urgent it is", "EMERGENCY"),
                new("{{RaisedByName}}", "Who raised it", "Anitha"),
                Reference,
                new("{{Waited}}", "How long it has been waiting, in words", "2 రోజుల"),
                new("{{TeamLeadNote}}", "A line naming the team lead who has not acknowledged. Empty unless this is a pastor alert", ""),
                new("{{ProtocolNote}}", "A line about safeguarding. Empty unless the reason requires it", ""),
                MyAssignmentsLink
            };

        private static string EscalationDefault(bool pastor) =>
            @"{{Title}}

🙏 Praise the Lord,

<b>{{PersonName}}</b> గారి కోసం ఒక escalation <b>{{Waited}}</b> నుండి pending లో ఉంది.

Reason: <b>{{Reason}}</b>
Tier: <b>{{Tier}}</b>
Raised by: {{RaisedByName}}
Ref: <code>{{Reference}}</code>
{{TeamLeadNote}}{{ProtocolNote}}
దయచేసి వెంటనే చూసి acknowledge చేయండి.

👉 {{MyAssignmentsLink}}";

        // ==================================================================
        // Lookup
        // ==================================================================

        public static TelegramTemplateDefinition? Find(string? code) =>
            string.IsNullOrWhiteSpace(code)
                ? null
                : All.FirstOrDefault(t => string.Equals(t.Code, code, StringComparison.Ordinal));

        public static bool IsKnown(string? code) => Find(code) is not null;

        /// <summary>
        /// Every token this scenario accepts: its own, plus the common ones.
        /// </summary>
        public static IReadOnlyList<TemplatePlaceholder> PlaceholdersFor(TelegramTemplateDefinition definition) =>
            definition.Placeholders.Concat(Common).ToList();
    }
}
