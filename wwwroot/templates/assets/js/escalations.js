$(document).ready(function () {

    const id = new URLSearchParams(window.location.search).get("id");

    if (!id) {
        alert("Invalid escalation ID");
        return;
    }

    const urlParams = new URLSearchParams(window.location.search);
    const role = urlParams.get('role');

    loadEscalation(id);



    $('#chkcrisifollwed').on('change', function () {
        if ($(this).is(':checked')) {
            $('#crisisFields').hide();
        } else {
            $('#crisisFields').show();
        }
    });
    // =========================
    // PASTOR UI CUSTOMIZATION
    // =========================
    if (role === 'pastor') {

        // Hide TL-only fields
        $('#outcomeSelect').closest('.form-group').hide();
        $('#resource').closest('.form-group').hide();
        $('#followUp').closest('.form-group').hide();
        $('#crisifollwed').closest('.form-group').hide();

        // Remove old options
        $('#statusSelect').empty();
        // <option value="ReEscalated">Re-Escalate to Team Lead</option>
        // Pastor options
        $('#statusSelect').append(`
            <option value="">Select Status</option>
            <option value="Resolved">Resolved</option>
            <option value="Closed">Closed</option>
           
        `);

        // Hide re-escalate button completely
        $('#reEscalateBtn').hide();

        $('.form-card-title').text('Pastor Resolution');

       
    }

    // =========================
    // LOAD DETAILS
    // =========================
    function loadEscalation(id) {

        $.get(API_BASE_URL + "/escalations/" + id)
            .done(res => {

                const e = res.data;

                $("#escId").text(e.escalationId);
                $("#personId").text(e.personId);
                $("#reason").text(e.escalationReason);
                $("#tier").text(e.escalationTier);
                $("#status").text(e.status);
                $("#desc").text(e.description);

            })
            .fail(() => alert("Failed to load"));

    }

    // =========================
    // ACKNOWLEDGE
    // =========================
    $("#ackBtn").click(function () {

        $.post(API_BASE_URL + "/escalations/acknowledge/" + id)

            .done(res => {

                showMessage(res.message, "success");

            })

            .fail(xhr => showMessage(getError(xhr), "error"));

    });

    // =========================
    // SUBMIT / RESOLVE
    // =========================
    $("#resolveBtn").click(function () {

        const isEscalateToPastor = $("#chkcrisifollwed").is(":checked");

        const payload = {
            escalationId: id,
            status: $("#statusSelect").val(),
            outcome: $("#outcomeSelect").val(),
            notes: $("#notes").val(),
            resourceConnected: $("#resource").val(),
            followUpScheduled: $("#followUp").is(":checked"),
            crisisProtocolFollowed: isEscalateToPastor,
            updatedByRole: role
        };

        // Checkbox checked => validations skip
        if (!isEscalateToPastor) {

            if (role === 'pastor') {

                if (!payload.status) {
                    showMessage("Status required", "error");
                    return;
                }

                payload.outcome = null;
                payload.resourceConnected = null;
                payload.followUpScheduled = false;
            }
            else {

                if (!payload.status || !payload.outcome) {
                    showMessage("Status & Outcome required", "error");
                    return;
                }
            }
        }

        $.ajax({
            url: API_BASE_URL + "/escalations/resolve",
            method: "POST",
            contentType: "application/json",
            data: JSON.stringify(payload),
            success: res => showMessage(res.message, "success"),
            error: xhr => showMessage(getError(xhr), "error")
        });

    });

    // =========================
    // HELPERS
    // =========================
    function showMessage(msg, type) {

        $("#message")
            .removeClass()
            .addClass(type)
            .text(msg);

    }

    function getError(xhr) {
        return xhr.responseJSON?.message || "Error occurred";
    }

});