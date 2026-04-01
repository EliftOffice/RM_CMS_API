// Utility function to format JSON response
function formatJsonResponse(data) {
    return JSON.stringify(data, null, 2);
}

// Utility function to display response
function displayResponse(containerId, responseType, message, data) {
    let alertClass = '';
    let icon = '';

    if (responseType === 'success') {
        alertClass = 'alert-success';
        icon = '<i class="fas fa-check-circle icon-success"></i>';
    } else if (responseType === 'error') {
        alertClass = 'alert-danger';
        icon = '<i class="fas fa-times-circle icon-error"></i>';
    } else if (responseType === 'warning') {
        alertClass = 'alert-warning';
        icon = '<i class="fas fa-exclamation-triangle icon-warning"></i>';
    } else {
        alertClass = 'alert-info';
        icon = '<i class="fas fa-info-circle"></i>';
    }

    let html = `
        <div class="alert ${alertClass}" role="alert">
            ${icon}
            <strong>${message}</strong>
        </div>
    `;

    if (data) {
        html += `
            <div class="response-data">
                <pre>${formatJsonResponse(data)}</pre>
            </div>
        `;
    }

    $(`#${containerId}`).html(html);
}
