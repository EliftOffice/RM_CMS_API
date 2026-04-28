$(document).ready(function () {

    // ================= LOGIN =================
    $('#loginForm').submit(function (e) {
        e.preventDefault();

        let mobile = $('#mobile').val().trim();

        if (!/^\d{10}$/.test(mobile)) {
            $('#errorMessage').text('Invalid mobile').show();
            return;
        }

        $.get(API_BASE_URL + `/volunteers/mobile/${mobile}`, function (res) {
            if (res.data && res.data.length > 0) {
                $('#successMessage').text('Login success').show();

                // 🔥 Extract volunteerId and navigate to Assignments
                const volunteer = res.data[0];
                const volunteerId = volunteer.volunteerId || volunteer.volunteer_id || volunteer.id;

                // Redirect to Assignments.html with volunteerId parameter
                setTimeout(() => {
                    window.location.href = `../../templates/Volunteers/Assignments.html?volunteerid=${volunteerId}`;
                }, 500); // Small delay to show success message

            } else {
                $('#errorMessage').text('Not found').show();
            }
        }).fail(function(xhr) {
            $('#errorMessage').text('Login failed. Please try again.').show();
        });
    });

    // ================= OPEN MODAL =================
    $('#forgotLink').click(function () {
        // Redirect to Assignments.html with volunteerId parameter
        setTimeout(() => {
            window.location.href = `UpdateMobile.html`;
        }, 500); // Small delay to show success message

    });

    $('#signUp').click(function () {
        // Redirect to Assignments.html with volunteerId parameter
        setTimeout(() => {
            window.location.href = `Volunteers.html`;
        }, 500); // Small delay to show success message

    });

   

    // ================= LOAD VOLUNTEERS =================
    function loadVolunteers() {
        $.get(API_BASE_URL + "/volunteers/GetVolunteersAsync", function (res) {

            let ddl = $('#volunteerDropdown');
            ddl.empty();
            ddl.append(`<option value="">-- Select Volunteer --</option>`);

            if (res.data) {
                res.data.forEach(v => {
                    ddl.append(`
                            <option value="${v.volunteer_id}">
                                ${v.first_name} ${v.last_name} (${v.volunteer_id})
                            </option>
                        `);
                });
            }
        });
    }

    

});