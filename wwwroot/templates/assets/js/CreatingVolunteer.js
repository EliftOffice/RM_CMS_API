$(document).ready(function () {

   

    // 🔹 LOAD ALL
    $("#loadBtn").click(loadVolunteers);

    function loadVolunteers() {
        baseUrl= API_BASE_URL + `/volunteers/CreateVolunteerAsync`,
        $.get(baseUrl, function (res) {
            let rows = "";

            res.data.forEach(v => {
                rows += `
                    <tr>
                        <td>${v.volunteerId}</td>
                        <td>${v.firstName} ${v.lastName}</td>
                        <td>${v.email}</td>
                       
                        <td>
                            <button onclick="editVolunteer('${v.volunteerId}')">Edit</button>
                            <button onclick="deleteVolunteer('${v.volunteerId}')">Delete</button>
                        </td>
                    </tr>
                `;
            });

            $("#volunteerTable").html(rows);
        });
    }

    // 🔹 SAVE
    $("#saveBtn").click(function () {

        const payload = getFormData();

        $.ajax({
            url: API_BASE_URL + `/volunteers/CreateVolunteerAsync`,
            method: "POST",
            contentType: "application/json",
            data: JSON.stringify(payload),
            success: function () {
                alert("Saved successfully");
                loadVolunteers();
                clearForm();
            }
        });
    });

    // 🔹 UPDATE
    $("#updateBtn").click(function () {

        const id = $("#volunteerId").val();
        const payload = getFormData();

        $.ajax({
            url: `${baseUrl}/${id}`,
            method: "PUT",
            contentType: "application/json",
            data: JSON.stringify(payload),
            success: function () {
                alert("Updated successfully");
                loadVolunteers();
                clearForm();
            }
        });
    });

    // 🔹 DELETE
    window.deleteVolunteer = function (id) {

        if (!confirm("Are you sure?")) return;

        $.ajax({
            url: `${baseUrl}/${id}`,
            method: "DELETE",
            success: function () {
                alert("Deleted");
                loadVolunteers();
            }
        });
    };

    // 🔹 EDIT (Load into form)
    window.editVolunteer = function (id) {

        $.get(`${baseUrl}/${id}`, function (res) {
            const v = res.data;

            $("#volunteerId").val(v.volunteerId);
            $("#firstName").val(v.firstName);
            $("#lastName").val(v.lastName);
            $("#email").val(v.email);
           
            $("#capacityMax").val(v.capacityMax);
        });
    };

    // 🔹 Helpers
    function getFormData() {
        return {
            firstName: $("#firstName").val(),
            lastName: $("#lastName").val(),
            email: $("#email").val(),
          
            capacityMax: parseInt($("#capacityMax").val())
        };
    }

    function clearForm() {
        $("input").val("");
    }

});