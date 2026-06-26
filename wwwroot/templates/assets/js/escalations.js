$(document).ready(function () {

    const id = new URLSearchParams(window.location.search).get("id");

    if (!id) {
        alert("Invalid escalation ID");
        return;
    }

    loadEscalation(id);

    // ✅ LOAD DETAILS
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

    // ✅ ACKNOWLEDGE
    $("#ackBtn").click(function () {

        $.post(API_BASE_URL + "/escalations/acknowledge/" + id)

            .done(res => {

                showMessage(res.message, "success");

                // ✅ hide details div
             //   $("#detailsDiv").hide();

                // ✅ show resolve div
               // $("#resolveDiv").show();

            })

            .fail(xhr => showMessage(getError(xhr), "error"));

    });

    // ✅ RESOLVE
    $("#resolveBtn").click(function () {

        const payload = {
            escalationId: id,
            status: $("#statusSelect").val(),
            outcome: $("#outcomeSelect").val(),
            notes: $("#notes").val(),
            resourceConnected: $("#resource").val(),
            followUpScheduled: $("#followUp").is(":checked")
        };

        if (!payload.status || !payload.outcome) {
            showMessage("Status & Outcome required", "error");
            return;
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

    // 🔧 HELPERS
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