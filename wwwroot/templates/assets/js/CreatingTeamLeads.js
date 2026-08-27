// Create a team lead.
//
// Rewired to the v2 API. The MVP posted to /TeamLeadDashBoards/save-team-lead, which
// wrote a `team_leads` row — a separate person record from `people` and `volunteers`,
// which is how the same human ended up stored three ways.
//
// In v2 a team lead is a PERSON with an account and the TEAM_LEAD role, so this makes
// two calls:
//
//   1. POST /api/admin/users  — the person, their login and the role
//   2. POST /api/teams        — the team they lead, carrying Max Volunteers
//
// Max Volunteers belongs to the team, not the person: v2 models the ceiling as
// team.max_members with team.lead_user_id pointing at the lead. Sending it on the user
// would have nowhere to land, and dropping the field would leave a control on the form
// that does nothing.
//
// Both calls need ADMIN. Creating an account that can read other people's pastoral
// records is not something a team lead grants themselves.

$(document).ready(function () {

    $("#saveBtn").click(function () {
        var $message = $("#message");
        $message.removeClass("error success").text("");

        var firstName = ($("#first_name").val() || "").trim();
        var lastName = ($("#last_name").val() || "").trim();
        var email = ($("#email").val() || "").trim();
        var phone = ($("#phone").val() || "").trim();
        var maxVolunteers = parseInt($("#max_volunteers").val(), 10) || 0;

        // The form offers TeamLead and Pastor; anything else falls back to TEAM_LEAD.
        var roleCode = String($("#role_type").val() || "TeamLead")
            .trim().toUpperCase() === "PASTOR" ? "PASTOR" : "TEAM_LEAD";

        if (!firstName || !lastName || !phone) {
            fail("First name, last name and phone are required.");
            return;
        }

        if (!isValidMobileNumber(phone)) {
            fail("Phone number must be exactly 10 digits.");
            return;
        }

        $("#saveBtn").prop("disabled", true);

        $.ajax({
            url: API_BASE_URL + "/admin/users",
            type: "POST",
            contentType: "application/json",
            data: JSON.stringify({
                givenName: firstName,
                familyName: lastName,
                email: email || null,
                // The mobile is also the username: every account in this system signs
                // in with a 10-digit number.
                mobile: phone,
                roleCode: roleCode,
                grantSystemAccess: true,
                mustChangePassword: true
            })
        })
        .done(function (res) {
            if (!res || res.responseType !== 0) {
                fail((res && res.message) || "That could not be saved.");
                $("#saveBtn").prop("disabled", false);
                return;
            }

            var account = res.data && (res.data.account || res.data);
            var accountId = account && account.id;

            if (maxVolunteers > 0 && accountId) {
                createTeam(accountId, firstName + " " + lastName, maxVolunteers, res.message);
            } else {
                succeed(res.message || "Team lead created.");
                $("#saveBtn").prop("disabled", false);
            }
        })
        .fail(function (xhr) {
            fail(errorFrom(xhr));
            $("#saveBtn").prop("disabled", false);
        });
    });

    /**
     * The team the new lead runs. A failure here is reported rather than swallowed:
     * the account exists either way, and an administrator who thinks a team was
     * created when it was not will wonder why nobody can be assigned to it.
     */
    function createTeam(accountId, personName, maxMembers, accountMessage) {
        $.ajax({
            url: API_BASE_URL + "/teams",
            type: "POST",
            contentType: "application/json",
            data: JSON.stringify({
                name: personName + " Team",
                leadAccountId: accountId,
                maxMembers: maxMembers
            })
        })
        .done(function (res) {
            if (!res || res.responseType !== 0) {
                succeed(accountMessage + " The team was not created: " + ((res && res.message) || "unknown reason."));
                return;
            }

            succeed(accountMessage + " Their team was created with room for " + maxMembers + ".");
        })
        .fail(function (xhr) {
            succeed(accountMessage + " The team was not created: " + errorFrom(xhr));
        })
        .always(function () {
            $("#saveBtn").prop("disabled", false);
        });
    }

    function succeed(text) {
        $("#message").removeClass("error").addClass("success").text(text);
        if (typeof showToast === "function") showToast(text, "success");
    }

    function fail(text) {
        $("#message").removeClass("success").addClass("error").text(text);
        if (typeof showToast === "function") showToast(text, "warning");
    }

    function errorFrom(xhr) {
        // 403 is worth naming: the most likely reason this screen fails is that the
        // person using it is a team lead rather than an administrator.
        if (xhr && xhr.status === 403) {
            return "Only an administrator can create team leads.";
        }

        var body = xhr && xhr.responseJSON;
        return (body && (body.message || body.detail || body.title)) || "Something went wrong.";
    }
});
