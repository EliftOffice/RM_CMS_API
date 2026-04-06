$(document).ready(function () {
    // Read volunteerId from querystring
    const urlParams = new URLSearchParams(window.location.search);
    const volunteerId = urlParams.get('volunteerId');
    $('#volunteerIdDisplay').text(volunteerId || '-');
    if (!volunteerId) return;

    // Load assignments
    function loadAssignments() {
        $.ajax({
            url: API_BASE_URL + `/volunteers/${volunteerId}/assignments`,
            method: 'GET',
            success: function (res) {
                if (!res || !res.data) {
                    $('#responseContainer').html('<div class="alert alert-warning">No assignments found.</div>');
                    return;
                }

                const rows = res.data.map(p => {
                    const nextAction = p.nextActionDate
                        ? new Date(p.nextActionDate).toLocaleString()
                        : '-';

                    const name = `${p.firstName} ${p.lastName}`;

                    return `
                    <tr>
                        <td>${p.personId}</td>
                        <td>${name}</td>
                        <td>${p.phone || '-'}</td>
                        <td>${p.email || '-'}</td>
                        <td>${p.campus || '-'}</td>
                        <td>${nextAction}</td>
                        <td>${p.followUpStatus}</td>
                        <td>
                            <button 
                                class="btn btn-sm btn-primary start-followup" 
                                data-person="${p.personId}" 
                                data-name="${name}">
                                Start
                            </button>
                        </td>
                    </tr>
                `;
                }).join('');

                $('#assignmentsTable tbody').html(rows);
            },
            error: function (xhr) {
                $('#responseContainer').html(
                    `<div class="alert alert-danger">Error loading assignments: ${xhr.responseText}</div>`
                );
            }
        });
    }

    loadAssignments();

    // Start follow-up
    $(document).on('click', '.start-followup', function () {
        const personId = $(this).data('person');
        const name = $(this).data('name');
        $('#person_id').val(personId);
        $('#volunteer_id').val(volunteerId);
        $('#followUpModal .modal-title').text(`Log Follow-up: ${name} (${personId})`);
        var modal = new bootstrap.Modal(document.getElementById('followUpModal'));
        modal.show();
    });

    $('#contact_status').on('change', function () {
        if ($(this).val() === 'Contacted') {
            $('#responseTypeGroup').show();
        } else {
            $('#responseTypeGroup').hide();
        }
    }).trigger('change');

    // Save follow-up
    $('#saveFollowUp').on('click', function () {
        const payload = {
            person_id: $('#person_id').val(),
            volunteer_id: $('#volunteer_id').val(),
            contact_method: $('#contact_method').val(),
            contact_status: $('#contact_status').val(),
            response_type: $('#response_type').val(),
            call_duration_min: parseInt($('#call_duration_min').val() || '0'),
            notes: $('#notes').val(),
            tags: $('#tags').val(),
            team_lead_id: 'TL001'


        };

        $.ajax({
            url: API_BASE_URL + '/followups/log-followup',
            method: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(payload),
            success: function (res) {
                $('#modalResponse').html('<div class="alert alert-success">Follow-up logged successfully.</div>');
                // refresh assignments
                loadAssignments();
                setTimeout(() => { $('#followUpModal .btn-close').click(); }, 1000);
            },
            error: function (xhr) {
                $('#modalResponse').html('<div class="alert alert-danger">Error logging follow-up.</div>');
            }
        });
    });
});
