$(document).ready(function () {

 

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

        // 🔹 Basic validation (frontend)
        if (!payload.firstName || !payload.email) {
            showMessage("First Name and Email are required", "error");
            return;
        }

        // 🔹 Validate phone number (10 digits only)
        if (payload.phone && !isValidMobileNumber(payload.phone)) {
            showMessage("Phone number must be exactly 10 digits", "error");
            return;
        }

        // 🔹 AJAX call
        $.ajax({
            url: API_BASE_URL +"/volunteers",
            method: "POST",
            contentType: "application/json",
            data: JSON.stringify(payload),

            success: function (res) {
                showMessage(res.message || "Volunteer created successfully", "success");

                console.log("Response:", res.data);

                // 🔹 Reset form
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