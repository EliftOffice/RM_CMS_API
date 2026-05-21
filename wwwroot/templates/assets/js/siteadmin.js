// siteadmin.js

$(document).ready(function () {
    loadSystemConfig();

    // Handle "Save All" button click
    $('#saveAllBtn').on('click', function () {
        saveAllConfigs();
    });

    // Handle individual "Save" button clicks (delegated event)
    $('#configTableBody').on('click', '.save-btn', function () {
        const key = $(this).data('key');
        const value = $(`#config-value-${key}`).val();
        const config = {
            configKey: key,
            configValue: value
        };
        saveSingleConfig(config);
    });

    // Handle broadcast button click
    $('#sendBroadcastBtn').on('click', function () {
        sendBroadcastMessage();
    });

    // Maintenance job buttons
    $('#sendRemindersBtn').on('click', function () {
        if (!confirm('Execute reminders job now?')) return;
        const btn = $(this);
        btn.prop('disabled', true).html('<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Running...');

        $.ajax({
            url: `${API_BASE_URL}/cornjobs/send-reminders`,
            type: 'POST',
            contentType: 'application/json',
            success: function (response) {
                // if API uses ApiResponse wrapper
                if (response && (response.responseType === 0 || response.success)) {
                    showToast(response.message || 'Reminders executed', 'success');
                } else {
                    const msg = response?.message || 'Reminders executed';
                    showToast(msg, 'success');
                }
            },
            error: function (xhr) {
                const errorMsg = xhr.responseJSON?.message || xhr.responseText || 'Server error';
                showToast(`Error executing reminders: ${errorMsg}`, 'error');
            },
            complete: function () {
                btn.prop('disabled', false).text('Send Reminders');
            }
        });
    });

    $('#assignNewPeopleBtn').on('click', function () {
        if (!confirm('Assign new people now?')) return;
        const btn = $(this);
        btn.prop('disabled', true).html('<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Running...');

        $.ajax({
            url: `${API_BASE_URL}/cornjobs/assign-new-people`,
            type: 'POST',
            contentType: 'application/json',
            success: function (response) {
                if (response && (response.responseType === 0 || response.success)) {
                    showToast(response.message || 'Assign job executed', 'success');
                } else {
                    const msg = response?.message || 'Assign job executed';
                    showToast(msg, 'success');
                }
            },
            error: function (xhr) {
                const errorMsg = xhr.responseJSON?.message || xhr.responseText || 'Server error';
                showToast(`Error assigning new people: ${errorMsg}`, 'error');
            },
            complete: function () {
                btn.prop('disabled', false).text('Assign New People');
            }
        });
    });
});

function loadSystemConfig() {
    const tableBody = $('#configTableBody');
    tableBody.html(`
        <tr>
            <td colspan="4" class="text-center">
                <div class="spinner-border" role="status">
                    <span class="visually-hidden">Loading...</span>
                </div>
            </td>
        </tr>
    `);

    $.ajax({
        url: `${API_BASE_URL}/systemconfig`,
        type: 'GET',
        contentType: 'application/json',
        success: function (response) {
            if (response.responseType === 0 && response.data) { // Success
                populateConfigTable(response.data);
            } else {
                showToast(`Failed to load config: ${response.message || 'Unknown error'}`, 'error');
                tableBody.html('<tr><td colspan="4" class="text-center text-danger">Error loading data.</td></tr>');
            }
        },
        error: function (xhr) {
            showToast(`Error fetching config: ${xhr.statusText || 'Server error'}`, 'error');
            tableBody.html('<tr><td colspan="4" class="text-center text-danger">Error loading data.</td></tr>');
        }
    });
}

function sendBroadcastMessage() {
    const message = $('#broadcastMessage').val();
    if (!message.trim()) {
        showToast('Message cannot be empty.', 'warning');
        return;
    }

    if (!confirm('Are you sure you want to send this message to all volunteers?')) {
        return;
    }

    const button = $('#sendBroadcastBtn');
    button.prop('disabled', true).html('<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Sending...');

    $.ajax({
        url: `${API_BASE_URL}/notifications/broadcast/volunteers`,
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify({ message: message }),
        success: function (response) {
            if (response.responseType === 0) { // Success
                showToast(response.message, 'success');
                $('#broadcastMessage').val(''); // Clear textarea on success
            } else {
                showToast(`Failed to send broadcast: ${response.message}`, 'error');
            }
        },
        error: function (xhr) {
            const errorMsg = xhr.responseJSON ? xhr.responseJSON.message : 'Server error';
            showToast(`An error occurred: ${errorMsg}`, 'error');
        },
        complete: function () {
            button.prop('disabled', false).html('Send to All Volunteers');
        }
    });
}

function populateConfigTable(configs) {
    const tableBody = $('#configTableBody');
    tableBody.empty(); // Clear spinner

    if (configs.length === 0) {
        tableBody.html('<tr><td colspan="4" class="text-center">No configuration settings found.</td></tr>');
        return;
    }

    configs.forEach(config => {
        const row = `
            <tr data-key="${config.configKey}">
                <td><code>${config.configKey}</code></td>
                <td class="table-description">${config.description || 'N/A'}</td>
                <td>
                    <input type="text" class="form-control" id="config-value-${config.configKey}" value="${config.configValue}">
                </td>
                <td class="text-center">
                    <button class="btn btn-sm btn-outline-secondary save-btn" data-key="${config.configKey}">Save</button>
                </td>
            </tr>
        `;
        tableBody.append(row);
    });
}

function saveSingleConfig(config) {
    // Show saving indicator
    const button = $(`.save-btn[data-key="${config.configKey}"]`);
    button.prop('disabled', true).html('<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>');

    $.ajax({
        url: `${API_BASE_URL}/systemconfig`,
        type: 'PUT',
        contentType: 'application/json',
        data: JSON.stringify([config]), // Send as an array
        success: function (response) {
            if (response.responseType === 0) { // Success
                showToast(`Setting '${config.configKey}' saved successfully.`, 'success');
            } else {
                showToast(`Failed to save '${config.configKey}': ${response.message}`, 'error');
            }
        },
        error: function (xhr) {
            showToast(`Error saving '${config.configKey}': ${xhr.statusText || 'Server error'}`, 'error');
        },
        complete: function () {
            // Restore button
            button.prop('disabled', false).html('Save');
        }
    });
}

function saveAllConfigs() {
    const configsToSave = [];
    $('#configTableBody tr').each(function () {
        const key = $(this).data('key');
        if (key) {
            const value = $(`#config-value-${key}`).val();
            configsToSave.push({
                configKey: key,
                configValue: value
            });
        }
    });

    if (configsToSave.length === 0) {
        showToast('No settings to save.', 'warning');
        return;
    }

    const saveAllButton = $('#saveAllBtn');
    saveAllButton.prop('disabled', true).html('<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Saving...');

    $.ajax({
        url: `${API_BASE_URL}/systemconfig`,
        type: 'PUT',
        contentType: 'application/json',
        data: JSON.stringify(configsToSave),
        success: function (response) {
            if (response.responseType === 0) { // Success
                showToast('All configuration settings saved successfully.', 'success');
            } else {
                showToast(`Failed to save settings: ${response.message}`, 'error');
            }
        },
        error: function (xhr) {
            showToast(`An error occurred while saving: ${xhr.statusText || 'Server error'}`, 'error');
        },
        complete: function () {
            saveAllButton.prop('disabled', false).html('Save All Changes');
        }
    });
}