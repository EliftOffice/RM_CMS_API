function setMessage(text, color) {
    if (window.showToast) {
        // map color keywords to types
        let type = 'success';
        if (!text) return;
        if (color === 'red' || color === 'danger' || color === 'error') type = 'error';
        if (color === 'green' || color === 'success') type = 'success';
        if (color === 'warning' || color === 'yellow') type = 'warning';
        showToast(text, type);
        return;
    }

    $("#message").text(text).css("color", color);
}

function resetForm() {
    $("#duration_min, #concerns_noted, #training_needs, #action_items").val("");
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

    // ✅ GET DATA FROM URL
    const params = new URLSearchParams(window.location.search);

    const volunteerId = params.get("id");
    const teamLeadId = params.get("teamLeadId");
    const volunteerName = params.get("vName");
    const teamLeadName = params.get("tName");

    // ✅ SET IDs (hidden)
    if (volunteerId) $("#volunteer_id").val(volunteerId);
    if (teamLeadId) $("#team_lead_id").val(teamLeadId);

    // ✅ SET NAMES (UI)
    if (volunteerName) $("#volunteer_name").val(decodeURIComponent(volunteerName));
    if (teamLeadName) $("#team_lead_name").val(decodeURIComponent(teamLeadName));

    console.log("INIT DATA:", {
        volunteerId,
        teamLeadId,
        volunteerName,
        teamLeadName
    });

    // Set today as default
    $("#check_in_date").val(new Date().toISOString().split("T")[0]);

    // Toggle capacity
    $("#capacity_adjustment").on("change", function () {
        $("#capBandWrap").toggle(this.checked);
        if (!this.checked) $("#new_capacity_band").val("");
    });

    // ── SAVE ───────────────────────────────────────────────
    $("#saveBtn").click(function () {
        setMessage("", "");
        const selectedOption = $("#new_capacity_band option:selected");

        const data = {
            volunteerId: $("#volunteer_id").val(),
            teamLeadId: $("#team_lead_id").val(),

            checkInDate: $("#check_in_date").val() || null,

            durationMin: parseInt($("#duration_min").val()) || null,

            meetingType: $("#meeting_type").val(),

            emotionalTone: $("#emotional_tone").val(),

            capacityAdjustment: $("#capacity_adjustment").is(":checked"),

            newCapacityBand: $("#capacity_adjustment").is(":checked")
                ? ($("#new_capacity_band").val() || null)
                : null,

            concernsNoted: $("#concerns_noted").val() || null,

            followUpNeeded: $("#follow_up_needed").is(":checked"),

            completionRateDiscussed: $("#completion_rate_discussed").is(":checked"),

            boundaryIssues: $("#boundary_issues").is(":checked"),

            trainingNeeds: $("#training_needs").val() || null,

            actionItems: $("#action_items").val() || null,

            nextCheckInDate: $("#next_check_in_date").val() || null,

            capacityMin: selectedOption.data("min") || null,

            capacityMax: selectedOption.data("max") || null
        };

        console.log("FINAL DATA:", JSON.stringify(data));

        if (!data.volunteerId || !data.teamLeadId || !data.emotionalTone) {
            setMessage("Volunteer, Team Lead and Emotional Tone are required.", "red");
            showToast("Volunteer, Team Lead and Emotional Tone are required.", "warning");
            
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
                showToast(res.message || "Check-in saved successfully!", "success");
                resetForm();
            },
            error: function (err) {
                console.error("ERROR:", err.responseText);
                const msg = err.responseJSON?.message || err.responseText || "An error occurred.";
                setMessage(msg, "red");
                showToast(msg, "error");
            },
            complete: function () {
                $("#saveBtn").prop("disabled", false).text("Save");
            }
        });
    });

});