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
            capacityBand: $("#capacityBand").val()
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

                $("#firstName").val('');
                $("#lastName").val('');
                $("#email").val('');
                $("#phone").val('');
                $("#teamLead").val('');
                $("#capacityBand").val('');
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

    // 🔹 LOAD TEAM LEADS (FIXED)
    function loadTeamLeads() {
        $.ajax({
            url: API_BASE_URL + "/TeamLeadDashBoards/team-leads",
            method: "GET",
            success: function (res) {

                let dropdown = $("#teamLead");
                dropdown.empty();
                dropdown.append('<option value="">Select TeamLead</option>');

                if (res.data && res.data.length > 0) {
                    res.data.forEach(function (tl) {
                        dropdown.append(
                            `<option value="${tl.team_lead_id}">${tl.name}</option>`
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