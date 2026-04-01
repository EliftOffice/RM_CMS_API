// Create Follow-up
$(function () {
    $('#createFollowUpForm').on('submit', function (e) {
        e.preventDefault();

        const followUpData = {
            personId: $('#fuPersonId').val(),
            volunteerId: $('#fuVolunteerId').val(),
            contactMethod: $('#fuContactMethod').val(),
            contactStatus: $('#fuContactStatus').val(),
            responseType: 'Normal',
            callDurationMin: 0,
            notes: 'Follow-up created from web interface'
        };

        $('#fuLoader').show();

        $.ajax({
            type: 'POST',
            url: `${API_BASE_URL}/followups`,
            contentType: 'application/json',
            data: JSON.stringify(followUpData),
            success: function (response) {
                displayResponse('followUpResponse', 'success', 'Follow-up created successfully', response.data);
                $('#createFollowUpForm')[0].reset();
            },
            error: function (xhr) {
                let errorMessage = 'Failed to create follow-up';
                if (xhr.responseJSON && xhr.responseJSON.message) {
                    errorMessage = xhr.responseJSON.message;
                }
                displayResponse('followUpResponse', 'error', errorMessage, xhr.responseJSON);
            },
            complete: function () {
                $('#fuLoader').hide();
            }
        });
    });
});
