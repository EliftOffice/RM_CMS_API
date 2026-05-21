$(function () {

    const statuses = ["", "NEW", "ASSIGNED", "CONTACTED", "RETRY PENDING", "ESCALATED", "COMPLETE", "UNRESPONSIVE"];

    function buildStatusFilter() {
        const sel = $('#statusFilter');
        if (!sel.length) return;
        sel.empty();
        statuses.forEach(s => sel.append(`<option value="${s}">${s || 'All'}</option>`));
        sel.on('change', function () { loadPeople($(this).val()); });
    }

    // 🔹 Load People (supports optional status filter)
    function loadPeople(status) {
        let url = API_BASE_URL + '/GetBasicPeopleAsync';
        if (status) url += '?status=' + encodeURIComponent(status);

        $.ajax({
            url: url,
            method: 'GET',
            success: function (res) {
                if (!res || !res.data) {
                    $('#peopleTable tbody').html('<tr><td colspan="8">No data</td></tr>');
                    return;
                }
                const rows = res.data.map(p => `
                    <tr>                       
                        <td>${(p.firstName || p.FirstName || '') + ' ' + (p.lastName || p.LastName || '')}</td>                       
                        <td>${p.phone || p.Phone || ''}</td>                        
                        <td>${p.ageRange || ''}</td>                        
                        <td>${p.address || ''}</td>                        
                        <td>${p.followUpStatus || ''}</td>                        
                        <td>${p.assignedVolunteer_Name || ''}</td>                        
                        <td>${p.nextActionDate || p.NextActionDate || ''}</td>
                        <td>
                            <button class="btn btn-sm btn-primary action-btn edit" data-id="${p.personId || p.PersonId}">Edit</button>
                            <button class="btn btn-sm btn-danger action-btn delete" data-id="${p.personId || p.PersonId}">Delete</button>
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

    // Wire up events etc.
    buildStatusFilter();
    loadPeople();

    $(document).on('click', '.edit', function () {
        const id = $(this).data('id');
        $.ajax({ url: API_BASE_URL + '/people/' + id, method: 'GET', success: function (res) {
                if (!res || !res.data) {
                    if (window.showToast) showToast('Person not found','warning'); else alert('Person not found');
                    return;
                }
                const p = res.data;
                $('#person_id').val(p.person_id || p.PersonId);
                $('#first_name').val(p.firstName || '');
                $('#last_name').val(p.lastName || '');
                $('#email').val(p.email || p.Email);
                $('#phone').val(p.phone || p.Phone);
                $('#campus').val(p.campus || p.Campus);
                $('#visit_type').val(p.visitType);
                $('#follow_up_status').val(p.follow_up_status || p.FollowUpStatus);
                $('#follow_up_priority').val(p.follow_up_priority || p.FollowUpPriority);
                $('#assigned_volunteer').val(p.assigned_volunteer || p.AssignedVolunteer);
                $('#interested_in').val(p.interested_in || p.InterestedIn);
                $('#prayer_requests').val(p.prayer_requests || p.PrayerRequests);
                $('#specific_needs').val(p.specific_needs || p.SpecificNeeds);

                var modal = new bootstrap.Modal(document.getElementById('editModal'));
                modal.show();
            }, error: function () { if (window.showToast) showToast('Error loading person','error'); else alert('Error'); }
        });
    });

    $('#savePerson').on('click', function () {
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
            interested_in: $('#interested_in').val(),
            prayer_requests: $('#prayer_requests').val(),
            specific_needs: $('#specific_needs').val()
        };

        $.ajax({
            url: API_BASE_URL + '/UpdateVisitor', method: 'PUT', contentType: 'application/json', data: JSON.stringify(payload), success: function (res) {
                if (res && res.data) {
                    if (window.showToast) showToast('Saved','success'); else $('#modalResponse').html('<div class="alert alert-success">Saved</div>');
                    setTimeout(() => { var modalEl = document.getElementById('editModal'); var modal = bootstrap.Modal.getInstance(modalEl); modal.hide(); loadPeople(); }, 700);
                } else {
                    if (window.showToast) showToast('Error saving','error'); else $('#modalResponse').html('<div class="alert alert-danger">Error</div>');
                }
            }, error: function () { if (window.showToast) showToast('API Error','error'); else $('#modalResponse').html('<div class="alert alert-danger">API Error</div>'); }
        });
    });

    $(document).on('click', '.delete', function () {
        const id = $(this).data('id');
        if (!confirm('Delete this person?')) return;
        $.ajax({ url: API_BASE_URL + '/people/' + id, method: 'DELETE', success: function () { if (window.showToast) showToast('Deleted','success'); loadPeople(); }, error: function () { if (window.showToast) showToast('Delete failed','error'); else alert('Delete failed'); } });
    });

});

