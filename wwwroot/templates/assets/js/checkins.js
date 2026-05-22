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

// Load capacity bands from server and bind to #new_capacity_band
function loadCapacityBands(selectedBand) {
    // clear existing
    const sel = $("#new_capacity_band");
    sel.html('<option value="">Loading...</option>');

    $.ajax({
        url: API_BASE_URL + '/volunteers/capacity-bands',
        method: 'GET',
        success: function (res) {
            sel.empty();
            sel.append('<option value="">Select Capacity</option>');
            if (res && Array.isArray(res.data)) {
                const bands = res.data;
                bands.forEach(b => {
                    const name = b.bandName || '';
                    const min = b.minPerWeek ?? b.min_per_week ?? '';
                    const max = b.maxPerWeek ?? b.max_per_week ?? '';
                    const label = `${name} (${min}-${max})`;
                    const option = $("<option></option>")
                        .attr('value', name)
                        .attr('data-min', min)
                        .attr('data-max', max)
                        .text(label);

                    if (selectedBand && selectedBand.toLowerCase() === name.toLowerCase()) {
                        option.prop('selected', true);
                    }

                    sel.append(option);
                });
            } else {
                // no data
                showToast(res?.message || 'No capacity bands found', 'warning');
            }
        },
        error: function (xhr) {
            sel.empty();
            sel.append('<option value="">Select Capacity</option>');
            const msg = xhr.responseJSON?.message || xhr.responseText || 'Error loading capacity bands';
            showToast(msg, 'error');
        }
    });
}

// Load volunteer details by id and set defaults
function loadVolunteerDetails(volunteerId) {
    if (!volunteerId) return;

    $.ajax({
        url: API_BASE_URL + '/volunteers/GetVolunteerDetails/' + encodeURIComponent(volunteerId),
        method: 'GET',
        success: function (res) {
            if (res && res.responseType === 0 && res.data) {
                const v = res.data;
                $('#volunteer_id').val(v.volunteerId || volunteerId);
                $('#team_lead_id').val(v.teamLeadId || '');
                $('#volunteer_name').text(`${v.firstName || ''} ${v.lastName || ''}`.trim());
                $('#team_lead_name').text(v.teamLeadName || '');

                // pre-select capacity band in dropdown once loaded
                loadCapacityBands(v.capacityBand);
            } else if (res && res.responseType === 1 && res.data) {
                const v = res.data;
                $('#volunteer_id').val(v.volunteerId || volunteerId);
                $('#team_lead_id').val(v.teamLeadId || '');
                $('#volunteer_name').text(`${v.firstName || ''} ${v.lastName || ''}`.trim());
                $('#team_lead_name').text(v.teamLeadName || '');
                loadCapacityBands(v.capacityBand);
                showToast(res.message || 'Volunteer loaded with warnings', 'warning');
            } else {
                showToast(res?.message || 'Volunteer not found', 'warning');
                // still load bands without selection
                loadCapacityBands();
            }
        },
        error: function (xhr) {
            const msg = xhr.responseJSON?.message || xhr.responseText || 'Error loading volunteer';
            showToast(msg, 'error');
            loadCapacityBands();
        }
    });
}

// ── INIT ──────────────────────────────────────────────────
$(document).ready(function () {

    // GET volunteerId from URL only
    const params = new URLSearchParams(window.location.search);
    const volunteerId = params.get("id");

    // load volunteer details and capacity bands
    if (volunteerId) {
        loadVolunteerDetails(volunteerId);
    } else {
        // fallback: just load bands
        loadCapacityBands();
    }

    // also set teamLead and volunteer id if present in query
    const teamLeadId = params.get("teamLeadId");
    if (teamLeadId) $("#team_lead_id").val(teamLeadId);

    const volunteerName = params.get("vName");
    if (volunteerName) $("#volunteer_name").text(decodeURIComponent(volunteerName));

    const teamLeadName = params.get("tName");
    if (teamLeadName) $("#team_lead_name").text(decodeURIComponent(teamLeadName));

    // Set today as default
    $("#check_in_date").val(new Date().toISOString().split("T")[0]);

    // Toggle capacity
    $("#capacity_adjustment").on("change", function () {
        $("#capBandWrap").toggle(this.checked);
       // if (!this.checked) $("#new_capacity_band").val("");
    });

    $("#follow_up_needed").change(function () {

        if ($(this).is(":checked")) {

            $("#nextcheckindate").show();
            $("#action_itemsDiv").show();

        } else {

            $("#nextcheckindate").hide();
            $("#action_itemsDiv").hide();

            // ✅ clear values
            $("#next_check_in_date").val("");
            $("#action_items").val("");

        }

    });

    // when capacity band changes, optionally update UI or store data
    $(document).on('change', '#new_capacity_band', function () {
        const opt = $(this).find('option:selected');
        // nothing mandatory to do now; save handler will read data-min/data-max
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

            newCapacityBand: $("#new_capacity_band").val() || null,

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

       

        if (!data.volunteerId || !data.teamLeadId || !data.emotionalTone) {
          //  setMessage("Volunteer, Team Lead and Emotional Tone are required.", "red");
            showToast("Emotional Tone are required.", "warning");
            
            return;
        }

        $("#saveBtn").prop("disabled", true).text("Saving…");

        $.ajax({
            url: API_BASE_URL + "/check-ins",
            type: "POST",
            contentType: "application/json",
            data: JSON.stringify(data),
            success: function (res) {

                showToast(res.message || "Check-in saved successfully!", "success");
                window.parent.bootstrap.Modal
                    .getInstance(window.parent.document.getElementById('esclationModel'))
                    .hide();

                resetForm();
              //  setMessage(res.message || "Check-in saved successfully!", "green");
             
               // resetForm();
            },
            error: function (err) {
                
                const msg = err.responseJSON?.message || err.responseText || "An error occurred.";
               // setMessage(msg, "red");
                showToast(msg, "error");
            },
            complete: function () {
                $("#saveBtn").prop("disabled", false).text("Save");
            }
        });
    });

});