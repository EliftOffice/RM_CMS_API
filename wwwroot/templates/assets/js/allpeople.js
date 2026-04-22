$(function () {
    function loadPeople() {
        $.ajax({
            url: API_BASE_URL + '/GetBasicPeopleAsync',
            method: 'GET',
            success: function (res) {
                if (!res || !res.data) {
                    $('#peopleTable tbody').html('<tr><td colspan="8">No data</td></tr>');
                    return;
                }
                const rows = res.data.map(p => `
                    <tr>
                        <td>${p.personId}</td>
                        <td>${p.firstName} ${p.lastName}</td>
                        <td>${p.email || ''}</td>
                        <td>${p.phone || ''}</td>
                        <td>${p.campus || ''}</td>
                        <td>${p.followUpStatus}</td>
                        <td>${p.nextActionDate ? new Date(p.nextActionDate).toLocaleString() : ''}</td>
                        <td>
                            <button class="btn btn-sm btn-primary action-btn edit" data-id="${p.personId}">Edit</button>
                            <button class="btn btn-sm btn-danger action-btn delete" data-id="${p.personId}">Delete</button>
                        </td>
                    </tr>
                `).join('');
                $('#peopleTable tbody').html(rows);
            },
            error: function () {
                $('#peopleTable tbody').html('<tr><td colspan="8">API Error</td></tr>');
            }
        });
    }

    loadPeople();

    $(document).on('click', '.edit', function () {
        const id = $(this).data('id');
        $.ajax({
            url: API_BASE_URL + '/people/' + id, method: 'GET', success: function (res) {
                if (!res || !res.data) return alert('Person not found');
                const p = res.data;
                $('#person_id').val(p.personId);
                $('#first_name').val(p.firstName);
                $('#last_name').val(p.lastName);
                $('#email').val(p.email);
                $('#phone').val(p.phone);
                $('#campus').val(p.campus);
                $('#visit_type').val(p.visitType);
                $('#follow_up_status').val(p.followUpStatus);
                $('#follow_up_priority').val(p.followUpPriority);
                $('#assigned_volunteer').val(p.assignedVolunteer);
                // Clear all first
                $('input[id^="interest_"], #Counseling').prop('checked', false);

                // Split values
                const interests = (p.interestedIn || "").split(',');

                // Set checked
                interests.forEach(val => {
                    val = val.trim();

                    if (val === "Membership") $('#interest_membership').prop('checked', true);
                    if (val === "Volunteering") $('#interest_volunteering').prop('checked', true);
                    if (val === "Small Groups") $('#interest_groups').prop('checked', true);
                    if (val === "Counseling") $('#Counseling').prop('checked', true);
                });
                $('#prayer_requests').val(p.prayerRequests);
                $('#specific_needs').val(p.specificNeeds);

                var modal = new bootstrap.Modal(document.getElementById('editModal'));
                modal.show();
            }, error: function () { alert('Error'); }
        });
    });

    $('#savePerson').on('click', function () {
        const interests = [];

        if ($('#interest_membership').is(':checked')) interests.push("Membership");
        if ($('#interest_volunteering').is(':checked')) interests.push("Volunteering");
        if ($('#interest_groups').is(':checked')) interests.push("Small Groups");
        if ($('#Counseling').is(':checked')) interests.push("Counseling");

        const interestedInValue = interests.join(',');

        const payload = {
            person_id: $('#person_id').val(),
            first_name: $('#first_name').val(),
            last_name: $('#last_name').val(),
            email: $('#email').val(),
            phone: $('#phone').val(),
            campus: $('#campus').val(),
            visit_type: $('#visit_type').val(),
            follow_up_status: $('#follow_up_status').val(),
            follow_up_priority: $('#follow_up_priority').val(),
            assigned_volunteer: $('#assigned_volunteer').val(),
            interested_in: interestedInValue,
            prayer_requests: $('#prayer_requests').val(),
            specific_needs: $('#specific_needs').val()
        };

        $.ajax({
            url: API_BASE_URL + '/UpdateVisitor', method: 'PUT', contentType: 'application/json', data: JSON.stringify(payload), success: function (res) {
                if (res && res.data) {
                    $('#modalResponse').html('<div class="alert alert-success">Saved</div>');
                    setTimeout(() => { var modalEl = document.getElementById('editModal'); var modal = bootstrap.Modal.getInstance(modalEl); modal.hide(); loadPeople(); }, 700);
                } else {
                    $('#modalResponse').html('<div class="alert alert-danger">Error</div>');
                }
            }, error: function () { $('#modalResponse').html('<div class="alert alert-danger">API Error</div>'); }
        });
    });

    $(document).on('click', '.delete', function () {
        const id = $(this).data('id');
        if (!confirm('Delete this person?')) return;
        // Implement delete endpoint later - for now simulate removal
        $.ajax({ url: API_BASE_URL + '/people/' + id, method: 'DELETE', success: function () { loadPeople(); }, error: function () { alert('Delete failed'); } });
    });
});

