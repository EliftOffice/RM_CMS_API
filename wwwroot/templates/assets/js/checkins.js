
function setMessage(text, color) {
    $("#message").text(text).css("color", color);
}

function resetForm() {
    $("#volunteer_id, #team_lead_id, #duration_min, #concerns_noted, #training_needs, #action_items").val("");
    $("#meeting_type").val("Monthly");
    $("#emotional_tone").val("");
    $("#new_capacity_band").val("");
    $("#capacity_adjustment, #follow_up_needed, #completion_rate_discussed, #boundary_issues").prop("checked", false);
    $("#capBandWrap").hide();
    $("#check_in_date").val(new Date().toISOString().split("T")[0]);
    $("#next_check_in_date").val("");
}

// ── INIT ──────────────────────────────────────────────────
$(document).ready(function () {

    // Set today as default check-in date
    $("#check_in_date").val(new Date().toISOString().split("T")[0]);

    // Show/hide capacity band when checkbox toggled
    $("#capacity_adjustment").on("change", function () {
        $("#capBandWrap").toggle(this.checked);
        if (!this.checked) $("#new_capacity_band").val("");
    });

    // ── SAVE  →  POST /api/check-ins ─────────────────────
    $("#saveBtn").click(function () {
        setMessage("", "");

        const data = {
            volunteerId: $("#volunteer_id").val().trim(),
            teamLeadId: $("#team_lead_id").val().trim(),
            checkInDate: $("#check_in_date").val() || null,
            durationMin: parseInt($("#duration_min").val()) || null,
            meetingType: $("#meeting_type").val(),
            emotionalTone: $("#emotional_tone").val(),
            capacityAdjustment: $("#capacity_adjustment").is(":checked"),
            newCapacityBand: $("#capacity_adjustment").is(":checked")
                ? ($("#new_capacity_band").val() || null) : null,
            concernsNoted: $("#concerns_noted").val() || null,
            followUpNeeded: $("#follow_up_needed").is(":checked"),
            completionRateDiscussed: $("#completion_rate_discussed").is(":checked"),
            boundaryIssues: $("#boundary_issues").is(":checked"),
            trainingNeeds: $("#training_needs").val() || null,
            actionItems: $("#action_items").val() || null,
            nextCheckInDate: $("#next_check_in_date").val() || null,
        };

        console.log("FINAL DATA:", JSON.stringify(data));

        // Required validation
        if (!data.volunteerId || !data.teamLeadId || !data.emotionalTone) {
            setMessage("Volunteer ID, Team Lead ID and Emotional Tone are required.", "red");
            return;
        }

        $("#saveBtn").prop("disabled", true).text("Saving…");

        $.ajax({
            url: API_BASE_URL + "/check-ins",
            type: "POST",
            contentType: "application/json",
            data: JSON.stringify(data),
            success: function (res) {
                console.log("SUCCESS:", res);
                setMessage(res.message || "Check-in saved successfully!", "green");
                resetForm();
            },
            error: function (err) {
                console.error("ERROR:", err.responseText);
                const msg = err.responseJSON?.message || err.responseText || "An error occurred.";
                setMessage(msg, "red");
            },
            complete: function () {
                $("#saveBtn").prop("disabled", false).text("Save");
            }
        });
    });

});