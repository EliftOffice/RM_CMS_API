let allData = [];
let filteredData = [];
let currentPage = 1;
const itemsPerPage = 10;

$(document).ready(function () {
    loadFinalDecisionList();

    // Search functionality
    $('#searchInput').on('keyup', function () {
        filterAndRender();
    });
});

async function loadFinalDecisionList() {
    showLoadingSpinner(true);
    try {
        const response = await $.ajax({
            url: API_BASE_URL + '/people/final-decision-list',
            type: 'GET',
            dataType: 'json'
        });

        if (response.responseType === 0) {
            allData = response.data.items || [];
            currentPage = 1;
            filterAndRender();
            showLoadingSpinner(false);
        } else {
            showErrorToast(response.message || 'Failed to load final decision list');
            showLoadingSpinner(false);
        }
    } catch (error) {
        console.error('Error loading final decision list:', error);
        showErrorToast('Error loading final decision list. Please try again.');
        showLoadingSpinner(false);
    }
}

function filterAndRender() {
    const searchTerm = $('#searchInput').val().toLowerCase();

    if (searchTerm === '') {
        filteredData = [...allData];
    } else {
        filteredData = allData.filter(item =>
            item.firstName.toLowerCase().includes(searchTerm) ||
            item.lastName.toLowerCase().includes(searchTerm)
        );
    }

    currentPage = 1;
    renderGrid();
    renderPagination();
}

function renderGrid() {
    const tableBody = $('#tableBody');
    tableBody.empty();

    if (filteredData.length === 0) {
        $('#emptyState').show();
        $('#gridContainer').hide();
        $('#paginationContainer').hide();
        return;
    }

    $('#emptyState').hide();
    $('#gridContainer').show();
    $('#paginationContainer').show();

    const startIdx = (currentPage - 1) * itemsPerPage;
    const endIdx = Math.min(startIdx + itemsPerPage, filteredData.length);
    const pageData = filteredData.slice(startIdx, endIdx);

    pageData.forEach(item => {
        const row = $('<tr></tr>');

        // Name
        const fullName = `${item.firstName} ${item.lastName}`;
        row.append(`<td><strong>${fullName}</strong></td>`);

        // Follow-ups 1-7
        for (let i = 1; i <= 7; i++) {
            const followUpKey = `followUp${i}`;
            const followUp = item[followUpKey];
            const responseType = followUp.responseType || '';
            const notes = followUp.notes || '';

            row.append(createFollowUpCell(responseType, notes));
        }

        // Final Decision
        const finalStatus = item.finalStatus || 'Pending Decision';
        const statusBadge = getStatusBadge(finalStatus);
        row.append(`<td>${statusBadge}</td>`);

        // Actions
        const actionsHtml = `
            <button class="btn btn-sm btn-primary action-btn" 
                    onclick="openEditModal('${item.personId}', '${item.firstName}', '${item.lastName}', '${item.finalStatus}')">
                Edit
            </button>
        `;
        row.append(`<td>${actionsHtml}</td>`);

        tableBody.append(row);
    });
}

function createFollowUpCell(responseType, notes) {

    if (!responseType) {
        return `
        <td>
            <span class="text-muted">-</span>
        </td>`;
    }

    const normalizedType = responseType.toUpperCase();

    const badgeClass = getBadgeClass(normalizedType);

    return `
<td style="min-width:200px">

    <div class="small text-muted fw-bold">
        Response Type
    </div>

    <span class="badge ${badgeClass}">
        ${responseType}
    </span>

    <div class="small text-muted fw-bold mt-2">
        Notes
    </div>

    <div class="small">
        ${notes || '-'}
    </div>

</td>`;
}

function getBadgeClass(responseType) {

    const type = responseType.toUpperCase();

    switch (type) {

        case 'NORMAL':
            return 'bg-success';

        case 'NEEDS_FOLLOWUP':
            return 'bg-warning text-dark';

        case 'TRY_AGAIN':
            return 'bg-primary';

        case 'CRISIS':
            return 'bg-danger';

        default:
            return 'bg-secondary';
    }
}

function getStatusBadge(status) {
    if (!status || status === '') {
        return '<span class="badge bg-warning text-dark">Pending Decision</span>';
    }

    if (status === 'PERMANENT') {
        return '<span class="badge bg-success">PERMANENT</span>';
    } else if (status === 'FAILED') {
        return '<span class="badge bg-danger">FAILED</span>';
    }

    return '<span class="badge bg-warning text-dark">Pending Decision</span>';
}

function renderPagination() {
    const totalPages = Math.ceil(filteredData.length / itemsPerPage);
    const paginationList = $('#paginationList');
    paginationList.empty();

    if (totalPages <= 1) {
        $('#paginationContainer').hide();
        return;
    }

    $('#paginationContainer').show();

    // Previous button
    const prevDisabled = currentPage === 1 ? 'disabled' : '';
    paginationList.append(`
        <li class="page-item ${prevDisabled}">
            <a class="page-link" href="javascript:void(0);" onclick="previousPage()">Previous</a>
        </li>
    `);

    // Page numbers
    for (let i = 1; i <= totalPages; i++) {
        const active = i === currentPage ? 'active' : '';
        paginationList.append(`
            <li class="page-item ${active}">
                <a class="page-link" href="javascript:void(0);" onclick="goToPage(${i})">${i}</a>
            </li>
        `);
    }

    // Next button
    const nextDisabled = currentPage === totalPages ? 'disabled' : '';
    paginationList.append(`
        <li class="page-item ${nextDisabled}">
            <a class="page-link" href="javascript:void(0);" onclick="nextPage()">Next</a>
        </li>
    `);
}

function previousPage() {
    if (currentPage > 1) {
        currentPage--;
        renderGrid();
        renderPagination();
    }
}

function nextPage() {
    const totalPages = Math.ceil(filteredData.length / itemsPerPage);
    if (currentPage < totalPages) {
        currentPage++;
        renderGrid();
        renderPagination();
    }
}

function goToPage(page) {
    currentPage = page;
    renderGrid();
    renderPagination();
}

function openEditModal(personId, firstName, lastName, currentStatus) {
    $('#personIdInput').val(personId);
    $('#personNameDisplay').val(`${firstName} ${lastName}`);
    $('#finalDecisionSelect').val(currentStatus || '');

    const modal = new bootstrap.Modal(document.getElementById('editFinalDecisionModal'));
    modal.show();
}

async function saveFinalDecision() {
    const personId = $('#personIdInput').val();
    const finalStatus = $('#finalDecisionSelect').val();

    if (!finalStatus) {
        showErrorToast('Please select a final decision');
        return;
    }

    try {
        const response = await $.ajax({
            url: API_BASE_URL + '/UpdateFinalStatus',
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({
                personId: personId,
                finalStatus: finalStatus
            })
        });

        if (response.responseType === 'Success') {
            showSuccessToast('Final decision updated successfully');

            // Close modal
            const modal = bootstrap.Modal.getInstance(document.getElementById('editFinalDecisionModal'));
            modal.hide();

            // Reload grid
            loadFinalDecisionList();
        } else {
            showErrorToast(response.message || 'Failed to update final decision');
        }
    } catch (error) {
        console.error('Error updating final decision:', error);
        showErrorToast('Error updating final decision. Please try again.');
    }
}

function showLoadingSpinner(show) {
    if (show) {
        $('#loadingSpinner').show();
        $('#gridContainer').hide();
        $('#emptyState').hide();
        $('#paginationContainer').hide();
    } else {
        $('#loadingSpinner').hide();
    }
}

function showSuccessToast(message) {
    $('#successToastBody').text(message);
    const toast = new bootstrap.Toast(document.getElementById('successToast'));
    toast.show();
}

function showErrorToast(message) {
    $('#errorToastBody').text(message);
    const toast = new bootstrap.Toast(document.getElementById('errorToast'));
    toast.show();
}

function escapeHtml(text) {
    const map = {
        '&': '&amp;',
        '<': '&lt;',
        '>': '&gt;',
        '"': '&quot;',
        "'": '&#039;'
    };
    return text.replace(/[&<>"']/g, m => map[m]);
}
