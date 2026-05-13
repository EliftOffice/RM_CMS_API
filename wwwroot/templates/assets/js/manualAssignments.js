$(function(){
    const statusOptions = ["", "NEW", "ASSIGNED", "CONTACTED", "RETRY PENDING", "ESCALATED", "COMPLETE", "UNRESPONSIVE"];

    function loadStatusOptions(){
        const sel = $('#filterStatus');
        sel.empty();
        statusOptions.forEach(s => sel.append(`<option value="${s}">${s || 'All'}</option>`));
    }

    function loadPeople(status){
        const url = API_BASE_URL + '/GetBasicPeopleAsync' + (status ? '?status=' + encodeURIComponent(status) : '');
        $.get(url, function (res) {

            if (!res || !Array.isArray(res.data) || res.data.length === 0) {
                $('#manualTable tbody').html(
                    `<tr><td colspan="6">${res?.message || 'No data found'}</td></tr>`
                );
                return;
            }

            const rows = res.data.map(p => `
        <tr>
            <td>${p.personId || p.PersonId}</td>
            <td>${(p.firstName || p.FirstName || '') + ' ' + (p.lastName || p.LastName || '')}</td>
            <td>${p.phone || p.Phone || ''}</td>
            
            <td>
                <button class="btn btn-sm btn-primary assign" 
                        data-id="${p.personId || p.PersonId}">
                    Assign
                </button>
            </td>
        </tr>
    `).join('');

            $('#manualTable tbody').html(rows);
        });
    }

   // $('#filterStatus').on('change', function(){ loadPeople($(this).val()); });

    $(document).on('click', '.assign', function(){
        const id = $(this).data('id');
        $('#assignPersonId').val(id);
        // load volunteers into select
        $.get(API_BASE_URL + '/volunteers/GetVolunteersAsync', function(res){
            const sel = $('#volunteerSelect'); sel.empty(); sel.append('<option value="">Select</option>');
            if(res && res.data){
                res.data.forEach(v => sel.append(`<option capMin="${v.capacityMin}" capMax="${v.capacityMax}" value="${v.volunteerId}">${v.firstName} ${v.lastName} (${v.volunteerId})</option>`));
            }
            var modal = new bootstrap.Modal(document.getElementById('assignModal'));
            modal.show();
        });
    });

    // show capacity band on change
    $(document).on('change', '#volunteerSelect', function () {
        const selected = $(this).find(':selected');

        const vid = selected.val();
        if (!vid) {
            $('#capacityInfo').text('-');
            return;
        }

        const capMin = selected.attr('capMin');
        const capMax = selected.attr('capMax');

        if (capMin || capMax) {
            $('#capacityInfo').text(
                (capMin || '-') + ' - ' + (capMax || '-')
            );
        } else {
            $('#capacityInfo').text('Unknown');
        }
    });

    // confirm assign
    $('#confirmAssign').on('click', function(){
        const personId = $('#assignPersonId').val();
        const volunteerId = $('#volunteerSelect').val();
        if(!personId || !volunteerId) {
            if (window.showToast) showToast('Select volunteer', 'warning'); else alert('Select volunteer');
            return;
        }

        $.ajax({
            url: API_BASE_URL + '/volunteers/manual-assign',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ PersonId: personId, VolunteerId: volunteerId }),
            success: function(res){
                if(res && (res.responseType === 1 || res.responseType === 'Success' || res.responseType === 'success')){
                    if (window.showToast) showToast('Assigned successfully', 'success'); else alert('Assigned successfully');
                    var modalEl = document.getElementById('assignModal'); var modal = bootstrap.Modal.getInstance(modalEl); modal.hide();
                    loadPeople($('#filterStatus').val());
                } else {
                    if (window.showToast) showToast('Assignment failed: ' + (res.message || 'error'), 'error'); else alert('Assignment failed: ' + (res.message || 'error'));
                }
            },
            error: function(){ if (window.showToast) showToast('API error', 'error'); else alert('API error'); }
        });
    });

    loadStatusOptions();
    loadPeople('NEW');
});
