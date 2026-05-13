$(document).ready(function () {

    // ── Init ──────────────────────────────────────────────────────────────────
    restrictMobileInput('phone');
    loadTeamLeads();
    GetTelegramURL();

    // ── Back buttons ──────────────────────────────────────────────────────────
    $('#backBtn').on('click', function () {
        window.location.href = 'Login.html';
    });

    // ── Load Team Leads ───────────────────────────────────────────────────────
    function loadTeamLeads() {
        $.ajax({
            url: API_BASE_URL + "/TeamLeadDashBoards/team-leads",
            method: "GET",
            success: function (res) {
                const dropdown = $("#teamLead");
                dropdown.empty();
                dropdown.append('<option value="">Select TeamLead</option>');

                if (res && res.data && res.data.length > 0) {
                    res.data.forEach(function (tl) {
                        const id =
                            tl.team_lead_id || tl.teamLeadId || tl.TeamLeadId ||
                            tl.team_lead || tl.teamLead || '';

                        let name =
                            tl.name || tl.team_lead_name || tl.teamLeadName ||
                            tl.TeamLeadName || '';

                        if (!name) {
                            const first = tl.first_name || tl.FirstName || tl.firstName || '';
                            const last = tl.last_name || tl.LastName || tl.lastName || '';
                            name = (first + ' ' + last).trim();
                        }

                        if (!name) name = tl.email || tl.Email || id || 'TeamLead';

                        dropdown.append(`<option value="${id}">${name}</option>`);
                    });
                }
            },
            error: function () {
                if (window.showToast) showToast("Failed to load TeamLeads", "error");
                else showMessage("Failed to load TeamLeads", "error");
            }
        });
    }

    // ── Get Telegram Bot URL ──────────────────────────────────────────────────
    function GetTelegramURL() {
        try {
            $.ajax({
                url: API_BASE_URL + '/volunteers/get-telegram-bot-url',
                method: 'GET',
                success: function (res) {
                    if (res && res.data) {
                        $("#telegramBotUrl").val(res.data);
                        $("#tel_boot_url").attr("href", res.data);
                    }
                },
                error: function () {
                    console.log("Error Getting Telegram URL");
                }
            });
        } catch (e) {
            console.log("GetTelegramURL exception:", e);
        }
    }

    // ── Get Chat ID button ────────────────────────────────────────────────────
    $('#getChatBtn').on('click', function (e) {
        e.preventDefault();
        $('#chatName').text('Loading...');

        $.ajax({
            url: API_BASE_URL + '/volunteers/get-latest-chat',
            method: 'GET',
            success: function (res) {
                if (res && res.data) {
                    const chatId =
                        res.data.chatId || res.data.ChatId || res.data.chat_id || '';
                    const name =
                        res.data.name || res.data.Name || res.data.name_display || '';

                    $('#telegramChatId').val(chatId);
                    $('#chatName').text(name ? name : 'No name');
                } else {
                    $('#chatName').text('No chat found');
                }
            },
            error: function () {
                $('#chatName').text('Error fetching chat');
            }
        });
    });

    // ── Copy Bot URL button ───────────────────────────────────────────────────
    $('#copyBotUrlBtn').on('click', function () {
        const url = $('#telegramBotUrl').val();
        navigator.clipboard.writeText(url);
        if (window.showToast) showToast('Url Copied', 'success'); else alert('Url Copied');
    });

    // ── Form submit ───────────────────────────────────────────────────────────
    $('#signupForm').on('submit', function (e) {
        e.preventDefault();
        hideMessages();

        const payload = {
            VolunteerId: "",
            firstName: $("#firstName").val().trim(),
            lastName: $("#lastName").val().trim(),
            email: $("#email").val().trim(),
            phone: $("#phone").val().trim(),
            teamLead: $("#teamLead").val().trim(),
            capacityBand: $("#capacityBand").val(),
            TelegramChatId: $("#telegramChatId").val().trim()
        };

        // Validation
        if (!payload.firstName || !payload.lastName || !payload.email) {
            if (window.showToast) showToast("First Name, Last Name, and Email are required.", "error");
            else showMessage("First Name, Last Name, and Email are required.", "error");
            return;
        }

        if (payload.phone && !isValidMobileNumber(payload.phone)) {
            if (window.showToast) showToast("Phone number must be exactly 10 digits.", "error");
            else showMessage("Phone number must be exactly 10 digits.", "error");
            return;
        }

        const btn = $('#submitBtn');
        btn.addClass('loading').prop('disabled', true);

        $.ajax({
            url: API_BASE_URL + "/volunteers",
            method: "POST",
            contentType: "application/json",
            data: JSON.stringify(payload),

            success: function (res) {
                btn.removeClass('loading').prop('disabled', false);

                const vid =
                    (res && res.data &&
                        (res.data.VolunteerId || res.data.volunteerId || res.data.volunteer_id)
                    ) || null;
                const chatId = payload.TelegramChatId;

                if (vid && chatId) {
                    // Link Telegram after account creation
                    $.ajax({
                        url: API_BASE_URL + '/volunteers/update-telegram',
                        method: 'PUT',
                        contentType: 'application/json',
                        data: JSON.stringify({ VolunteerId: vid, TelegramChatId: chatId }),
                        success: function () {
                            if (window.showToast) showToast('Account created and Telegram linked. Redirecting...', 'success');
                            else showMessage('Account created and Telegram linked. Redirecting...', 'success');
                            setTimeout(() => { window.location.href = 'Login.html'; }, 1800);
                        },
                        error: function () {
                            if (window.showToast) showToast('Account created but failed to link Telegram. Redirecting...', 'warning');
                            else showMessage('Account created but failed to link Telegram. Redirecting...', 'success');
                            setTimeout(() => { window.location.href = 'Login.html'; }, 1800);
                        }
                    });
                } else {
                    if (window.showToast) showToast(res.message || 'Account created successfully! Redirecting...', 'success');
                    else showMessage(res.message || 'Account created successfully! Redirecting...', 'success');
                    clearForm();
                    setTimeout(() => { window.location.href = 'Login.html'; }, 1800);
                }
            },

            error: function (xhr) {
                btn.removeClass('loading').prop('disabled', false);
                let msg = "Something went wrong.";
                if (xhr.responseJSON && xhr.responseJSON.message) {
                    msg = xhr.responseJSON.message;
                }
                if (window.showToast) showToast(msg, 'error'); else showMessage(msg, 'error');
            }
        });
    });

    // ── Helpers ───────────────────────────────────────────────────────────────
    function showMessage(message, type) {
        if (window.showToast) {
            showToast(message, type === 'error' ? 'error' : 'success');
            return;
        }

        if (type === 'error') {
            $('#errorMessage').text(message).show();
        } else {
            $('#successMessage').text(message).show();
        }
    }

    function hideMessages() {
        $('#errorMessage').hide().text('');
        $('#successMessage').hide().text('');
    }

    function clearForm() {
        $("#firstName, #lastName, #email, #phone, #telegramChatId").val('');
        $("#teamLead, #capacityBand").val('');
    }


   
});