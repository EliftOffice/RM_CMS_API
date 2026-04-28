$(document).ready(function () {

    loadTeamLeads();

    // 🔹 Submit form
    $("#submitBtn").click(function () {

        const payload = {
            VolunteerId: "",
            firstName: $("#firstName").val().trim(),
            lastName: $("#lastName").val().trim(),
            email: $("#email").val().trim(),
            phone: $("#phone").val().trim(),
            teamLead: $("#teamLead").val().trim(),
            capacityBand: $("#capacityBand").val(),
            TelegramChatId: $("#telegramChatId").val()
        };

        if (!payload.firstName || !payload.email) {
            showMessage("First Name and Email are required", "error");
            return;
        }

        if (payload.phone && !isValidMobileNumber(payload.phone)) {
            showMessage("Phone number must be exactly 10 digits", "error");
            return;
        }

        $.ajax({
            url: API_BASE_URL + "/volunteers",
            method: "POST",
            contentType: "application/json",
            data: JSON.stringify(payload),

            success: function (res) {


                showMessage(res.message || "Volunteer created successfully", "success");

                // Clear form only on success
                $("#firstName").val('');
                $("#lastName").val('');
                $("#email").val('');
                $("#phone").val('');
                $("#teamLead").val('');
                $("#capacityBand").val('');
                $("#telegramChatId").val('');
            },

            error: function (xhr) {
                let msg = "Something went wrong";

                if (xhr.responseJSON && xhr.responseJSON.message) {
                    msg = xhr.responseJSON.message;
                }

                showMessage(msg, "error");
            }
        });
    });

    // 🔹 LOAD TEAM LEADS (robust)
    function loadTeamLeads() {
        $.ajax({
            url: API_BASE_URL + "/TeamLeadDashBoards/team-leads",
            method: "GET",
            success: function (res) {

                let dropdown = $("#teamLead");
                dropdown.empty();
                dropdown.append('<option value="">Select TeamLead</option>');

                if (res && res.data && res.data.length > 0) {
                    res.data.forEach(function (tl) {
                        // handle different casings / shapes
                        const id = tl.team_lead_id || tl.teamLeadId || tl.TeamLeadId || tl.team_lead_id || tl.team_lead || tl.teamLead || '';
                        let name = tl.name || tl.team_lead_name || tl.teamLeadName || tl.TeamLeadName || '';

                        if (!name) {
                            const first = tl.first_name || tl.FirstName || tl.firstName || '';
                            const last = tl.last_name || tl.LastName || tl.lastName || '';
                            name = (first + ' ' + last).trim();
                        }

                        if (!name) name = tl.email || tl.Email || id || 'TeamLead';

                        dropdown.append(
                            `<option value="${id}">${name}</option>`
                        );
                    });
                }
            },
            error: function () {
                showMessage("Failed to load TeamLeads", "error");
            }
        });
    }

    // 🔹 Message helper
    function showMessage(message, type) {
        $("#message")
            .removeClass("success error")
            .addClass(type)
            .text(message)
            .fadeIn();

        setTimeout(() => {
            $("#message").fadeOut();
        }, 3000);
    }

});