namespace RM_CMS.Security
{
    /// <summary>Named rate-limiting policies, registered in Program.cs.</summary>
    public static class RateLimitPolicies
    {
        /// <summary>Credential submission — the brute-force surface. Tightest limit.</summary>
        public const string Login = "rl-login";

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
