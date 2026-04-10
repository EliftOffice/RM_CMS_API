// ================= LOAD VOLUNTEERS =================
$(document).ready(function () {
    loadVolunteers();
});

function loadVolunteers() {
    $.ajax({
        url: API_BASE_URL + "/volunteers/GetVolunteersAsync",
        method: "GET",

        success: function (res) {
            if (!res || !res.data) return;

            let dropdown = $('#volunteerDropdown');
            dropdown.empty();
            dropdown.append(`<option value="">-- Select Volunteer --</option>`);

            res.data.forEach(v => {
                dropdown.append(
                    `<option value="${v.volunteerId}">
                        ${v.firstName} ${v.lastName}
                    </option>`
                );
            });
        },

        error: function () {
            $('#forgotError').text('Failed to load volunteers').show();
        }
    });
}

// ================= UPDATE MOBILE =================
$('#forgotForm').submit(function (e) {
    e.preventDefault();

    $('#forgotError').hide();
    $('#forgotSuccess').hide();

    let volunteerId = $('#volunteerDropdown').val();
    let mobile = $('#newMobile').val().trim();
    let confirmMobile = $('#confirmMobile').val().trim();

    if (!volunteerId || !mobile || !confirmMobile) {
        $('#forgotError').text('All fields required').show();
        return;
    }

    if (!/^\d{10}$/.test(mobile)) {
        $('#forgotError').text('Mobile must be 10 digits').show();
        return;
    }

    if (mobile !== confirmMobile) {
        $('#forgotError').text('Mobile numbers do not match').show();
        return;
    }

    $.ajax({
        url: API_BASE_URL + "/volunteers/update-mobile",
        method: "PUT",
        contentType: "application/json",

        data: JSON.stringify({
            volunteerId: volunteerId,
            newMobile: mobile
        }),

        success: function (res) {
            if (res.responseType === 1) {
                $('#forgotSuccess').text('Updated successfully').show();
                $('#newMobile').val('');
                $('#confirmMobile').val('');
            } else {
                $('#forgotError').text(res.message).show();
            }
        },

        error: function () {
            $('#forgotError').text('Error updating').show();
        }
    });
});