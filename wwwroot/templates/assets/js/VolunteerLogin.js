
$(document).ready(function () {
    sessionStorage.setItem("login_otp", "");
    setLoginSession(false);

    let currentVolunteerId = "";
    let currentMobile = "";

    // ================= LOGIN =================
    $('#loginForm').submit(function (e) {

        if (!sessionStorage.getItem("login_otp") == "") return false;
        debugger;

        e.preventDefault();

        $('#errorMessage').hide();
        $('#successMessage').hide();

        let mobile = $('#mobile').val().trim();

        if (!/^\d{10}$/.test(mobile)) {

            showToast('Invalid mobile number', 'warning');
            return;
        }

        $.get(API_BASE_URL + `/volunteers/GetVolunteersByMobileAsyncV1/${mobile}`, function (res) {

            if (res.data && res.data.length > 0) {

                const volunteer = res.data[0];

                currentVolunteerId =
                    volunteer.id;

                currentMobile = mobile;

                // Demo OTP
                const otp = volunteer.otp;

                sessionStorage.setItem("login_otp", otp);
                sessionStorage.setItem("login_role", volunteer.role);

                showToast('📩 OTP sent to Telegram successfully', 'success');

                // Hide mobile section
                $('#mobileSection').hide();

                // Show OTP section
                $('#otpSection').fadeIn();

                // Focus first OTP
                $('.otp-input').first().focus();

            } else {

                showToast('Volunteer not found', 'error');
            }

        }).fail(function () {

            showToast('Login failed. Please try again.', 'error');
        });

    });

    // ================= OTP AUTO MOVE + AUTO VERIFY =================
    $(document).on('input', '.otp-input', function () {

        // Move to next input
        if ($(this).val().length === 1) {
            $(this).next('.otp-input').focus();
        }

        // Get complete OTP
        let enteredOtp = "";

        $('.otp-input').each(function () {
            enteredOtp += $(this).val();
        });

        // Auto verify when 4 digits entered
        if (enteredOtp.length === 4) {
            $('#loadingOverlay').css('display', 'flex');
            const savedOtp = sessionStorage.getItem("login_otp");
            const role = sessionStorage.getItem("login_role");

            if (enteredOtp === savedOtp) {

                if (role) {

                    setLoginSession(true);

                    let redirectUrl = "";

                    if (role === "volunteer") {

                        redirectUrl = `../../templates/Volunteers/Assignments.html?volunteerid=${currentVolunteerId}`;

                    }
                    else if (role === "TeamLead") {

                        redirectUrl = `../../templates/TeamLeads/TeamLeadDashboard.html?teamleadid=${currentVolunteerId}`;

                    }
                    else if (role === "Pastor") {

                        redirectUrl = `../../templates/Pastor/Dashboard.html`;

                    }
                    else if (role === "Admin") {

                        redirectUrl = `../../templates/Admin/siteadmin.html`;

                    }
                    else {
                        $('#loadingOverlay').hide();
                        showToast('Unknown role. Contact admin', 'error');
                        return;
                    }

                    showToast('✅ Login successful', 'success');

                    setTimeout(() => {

                        window.location.href = redirectUrl;

                    }, 800);
                }

            } else {
                showToast('Invalid OTP', 'error');
                $('#loadingOverlay').hide();
            }
        }
    });

    // ================= OPEN MODAL =================
    $('#forgotLink').click(function () {

        window.location.href = `UpdateMobile.html`;

    });

    $('#signUp').click(function () {

        window.location.href = `Volunteers.html`;

    });

    // ================= LOAD VOLUNTEERS =================
    function loadVolunteers() {

        $.get(API_BASE_URL + "/volunteers/GetVolunteersAsync", function (res) {

            let ddl = $('#volunteerDropdown');

            ddl.empty();

            ddl.append(`
                <option value="">
                    -- Select Volunteer --
                </option>
            `);

            if (res.data) {

                res.data.forEach(v => {

                    ddl.append(`
                        <option value="${v.volunteer_id}">
                            ${v.first_name} ${v.last_name}
                            (${v.volunteer_id})
                        </option>
                    `);

                });

            }

        });

    }

    function setLoginSession(isLogin) {

        sessionStorage.setItem("isLoggedIn", isLogin);
    }

    // ================= KEY NAVIGATION =================
    $(document).on('keydown', '.otp-input', function (e) {

        // Backspace -> previous input
        if (e.key === "Backspace" && $(this).val() === '') {

            $(this).prev('.otp-input').focus();
        }

        // Left Arrow -> previous input
        if (e.key === "ArrowLeft") {

            $(this).prev('.otp-input').focus();
        }

        // Right Arrow -> next input
        if (e.key === "ArrowRight") {

            $(this).next('.otp-input').focus();
        }

    });

});