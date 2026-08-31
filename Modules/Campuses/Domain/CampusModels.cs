namespace RM_CMS.Modules.Campuses.Domain
{
    /// <summary>
    /// A physical site the church runs.
    ///
    /// Campus is the tenancy boundary: a person, a volunteer, a team, a case and an
    /// escalation all belong to one, and <c>CurrentIdentity.CanAccessCampus</c> is
    /// what stops an account at one site reading another's pastoral records.
    ///
    /// The table existed from the first schema and was seeded with a single row, but
    /// nothing could create a second one — there was no endpoint and no screen. That
    /// made the whole boundary untestable in practice, because there was never
    /// anything on the other side of it.
    /// </summary>
    public sealed class Campus
    {
        public long Id { get; set; }
        public string PublicId { get; set; } = string.Empty;

        /// <summary>Short handle ('ONGOLE'). Unique, uppercase, and never reused.</summary>
        public string Code { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        /// <summary>IANA zone id ('Asia/Kolkata'). Drives local-time display.</summary>
        public string Timezone { get; set; } = "Asia/Kolkata";

        public bool IsActive { get; set; } = true;

        // ---- derived, counted at read time ----
        //
        // These are what make the retire guard explainable: "3 volunteers and 12 open
        // cases still belong to Ongole" is actionable, "cannot retire" is not.

        public int PersonCount { get; set; }
        public int VolunteerCount { get; set; }
        public int TeamCount { get; set; }
        public int OpenCaseCount { get; set; }

        public DateTime CreatedAt { get; set; }
        public int RowVersion { get; set; }

        /// <summary>Nothing is attached, so retiring it costs nobody anything.</summary>
        public bool CanRetire =>
            PersonCount == 0 && VolunteerCount == 0 && TeamCount == 0 && OpenCaseCount == 0;
    }
}
