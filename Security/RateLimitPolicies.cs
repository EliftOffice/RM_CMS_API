namespace RM_CMS.Security
{
    /// <summary>Named rate-limiting policies, registered in Program.cs.</summary>
    public static class RateLimitPolicies
    {
        /// <summary>Credential submission — the brute-force surface. Tightest limit.</summary>
        public const string Login = "rl-login";

        /// <summary>
        /// "Does this mobile number need a password?" — asked by the login screen
        /// before anyone signs in.
        /// </summary>
        /// <remarks>
        /// Its own bucket rather than sharing <see cref="Login"/>, because it runs on
        /// the way TO a sign-in rather than being one. Sharing would mean every
        /// successful sign-in spent two of the five permitted attempts, and a person who
        /// mistyped their number once would be locked out before their first real try.
        ///
        /// Looser than Login on purpose, and defensible: this endpoint accepts no
        /// credential, so there is nothing here to brute-force. It does reveal which
        /// numbers are passwordless, but no rate limit fixes that — telling the browser
        /// to skip the password box IS the answer, and it is a cost of the feature.
        /// </remarks>
        public const string LoginMethod = "rl-login-method";

        /// <summary>
        /// The sign-in screen asking whether the Telegram tap has arrived yet.
        /// </summary>
        /// <remarks>
        /// Sized from the actual client behaviour, which is what the earlier sharing of
        /// <see cref="LoginMethod"/> was not. A challenge lives three minutes and the
        /// screen polls every two seconds, so ONE ordinary sign-in spends about ninety
        /// permits. Against a bucket of twenty that ran out after forty seconds, and
        /// every later poll came back 429 — which the page cannot tell apart from "not
        /// tapped yet", so an approved sign-in sat on "waiting" forever.
        ///
        /// Loose on purpose and safe to be: the poll carries no credential, and the
        /// challenge id it quotes was handed to a browser that already passed the first
        /// factor. The global ceiling still applies underneath.
        /// </remarks>
        public const string VerifyPoll = "rl-verify-poll";

        /// <summary>Token refresh. Legitimate clients hit this roughly once per access-token lifetime.</summary>
        public const string Refresh = "rl-refresh";

        /// <summary>Password changes, account administration, notification broadcasts.</summary>
        public const string Sensitive = "rl-sensitive";

        /// <summary>
        /// The public website's form endpoint — the only anonymous write in the
        /// application, and the only one a stranger can reach without a token.
        /// </summary>
        public const string PublicForm = "rl-public-form";

        /// <summary>Default ceiling applied to the whole API.</summary>
        public const string Global = "rl-global";
    }
}
