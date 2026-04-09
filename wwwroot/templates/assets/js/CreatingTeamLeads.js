$(document).ready(function () {

    $("#saveBtn").click(function () {

        var data = {
            FirstName: $("#first_name").val()?.trim(),
            LastName: $("#last_name").val()?.trim(),
            Email: $("#email").val()?.trim(),
            Phone: $("#phone").val()?.trim(),
            RoleType: $("#role_type").val() || "TeamLead",
            MaxVolunteers: parseInt($("#max_volunteers").val()) || 0
        };

        console.log("DATA:", JSON.stringify(data));

        if (!data.FirstName || !data.LastName || !data.Phone) {
            $("#message").text("First Name, Last Name and Phone are required")
                .addClass("error");
            return;
        }

        $.ajax({
            url: API_BASE_URL + '/TeamLeadDashBoards/save-team-lead',
            type: "POST",
            contentType: "application/json",
            data: JSON.stringify(data),

            success: function (res) {
                console.log("SUCCESS:", res);
                $("#message").text(res.message)
                    .removeClass("error")
                    .addClass(res.responseType === 1 ? "success" : "error");
            },

            error: function (err) {
                console.error("ERROR:", err);
                console.error("RESPONSE:", err.responseText);

                $("#message").text(err.responseText || "Bad Request")
                    .addClass("error");
            }
        });
    });

});