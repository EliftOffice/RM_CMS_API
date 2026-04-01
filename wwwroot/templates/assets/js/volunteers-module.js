// Create Volunteer
$(function () {
    $('#createVolunteerForm').on('submit', function (e) {
        e.preventDefault();

        const volunteerData = {
            firstName: $('#volFirstName').val(),
            lastName: $('#volLastName').val(),
            email: $('#volEmail').val(),
            status: $('#volStatus').val(),
            level: 'Level 1',
            startDate: new Date().toISOString().split('T')[0],
            capacityBand: 'Balanced',
            capacityMin: 2,
            capacityMax: 3
        };

        $('#volLoader').show();

        $.ajax({
            type: 'POST',
            url: `${API_BASE_URL}/volunteers`,
            contentType: 'application/json',
            data: JSON.stringify(volunteerData),
            success: function (response) {
                displayResponse('volunteerResponse', 'success', 'Volunteer created successfully', response.data);
                $('#createVolunteerForm')[0].reset();
            },
            error: function (xhr) {
                let errorMessage = 'Failed to create volunteer';
                if (xhr.responseJSON && xhr.responseJSON.message) {
                    errorMessage = xhr.responseJSON.message;
                }
                displayResponse('volunteerResponse', 'error', errorMessage, xhr.responseJSON);
            },
            complete: function () {
                $('#volLoader').hide();
            }
        });
    });
});
