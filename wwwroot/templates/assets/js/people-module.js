// Assign Person to Volunteer
$(function () {
    $('#assignPersonForm').on('submit', function (e) {
        e.preventDefault();
        const personId = $('#personId').val();

        $('#assignLoader').show();
        $('#assignBtn').prop('disabled', true);

        $.ajax({
            type: 'POST',
            url: `${API_BASE_URL}/peoples/${personId}/assign`,
            contentType: 'application/json',
            success: function (response) {
                if (response.responseType === 'Success' || response.responseType === 'Warning') {
                    displayResponse('assignmentResponse', 'success', response.message, response.data);
                } else {
                    displayResponse('assignmentResponse', 'error', response.message, response.data);
                }
            },
            error: function (xhr) {
                let errorMessage = 'An error occurred';
                if (xhr.responseJSON && xhr.responseJSON.message) {
                    errorMessage = xhr.responseJSON.message;
                }
                displayResponse('assignmentResponse', 'error', errorMessage, xhr.responseJSON);
            },
            complete: function () {
                $('#assignLoader').hide();
                $('#assignBtn').prop('disabled', false);
            }
        });
    });
});
