//$(document).ready(function () {

//    // ================= LOGIN =================
//    $('#loginForm').submit(function (e) {
//        e.preventDefault();

//        let mobile = $('#mobile').val().trim();

//        if (!/^\d{10}$/.test(mobile)) {
//            $('#errorMessage').text('Invalid mobile').show();
//            return;
//        }

//        $.get(API_BASE_URL + `/volunteers/mobile/${mobile}`, function (res) {
//            if (res.data && res.data.length > 0) {
//                $('#successMessage').text('Login success').show();

//                // 🔥 Extract volunteerId and navigate to Assignments
//                const volunteer = res.data[0];
//                const volunteerId = volunteer.volunteerId || volunteer.volunteer_id || volunteer.id;

//                // Redirect to Assignments.html with volunteerId parameter
//                setTimeout(() => {
//                    window.location.href = `../../templates/Volunteers/Assignments.html?volunteerid=${volunteerId}`;
//                }, 500); // Small delay to show success message

//            } else {
//                $('#errorMessage').text('Not found').show();
//            }
//        }).fail(function(xhr) {
//            $('#errorMessage').text('Login failed. Please try again.').show();
//        });
//    });

//    // ================= OPEN MODAL =================
//    $('#forgotLink').click(function () {
//        // Redirect to Assignments.html with volunteerId parameter
//        setTimeout(() => {
//            window.location.href = `UpdateMobile.html`;
//        }, 500); // Small delay to show success message

//    });

//    $('#signUp').click(function () {
//        // Redirect to Assignments.html with volunteerId parameter
//        setTimeout(() => {
//            window.location.href = `Volunteers.html`;
//        }, 500); // Small delay to show success message

//    });



//    // ================= LOAD VOLUNTEERS =================
//    function loadVolunteers() {
//        $.get(API_BASE_URL + "/volunteers/GetVolunteersAsync", function (res) {

//            let ddl = $('#volunteerDropdown');
//            ddl.empty();
//            ddl.append(`<option value="">-- Select Volunteer --</option>`);

//            if (res.data) {
//                res.data.forEach(v => {
//                    ddl.append(`
//                            <option value="${v.volunteer_id}">
//                                ${v.first_name} ${v.last_name} (${v.volunteer_id})
//                            </option>
//                        `);
//                });
//            }
//        });
//    }



//});




$(document).ready(function () {

    let currentVolunteerId = "";
    let currentMobile = "";

    // ================= LOGIN =================
    $('#loginForm').submit(function (e) {

        e.preventDefault();

        $('#errorMessage').hide();
        $('#successMessage').hide();

        let mobile = $('#mobile').val().trim();

        if (!/^\d{10}$/.test(mobile)) {

            showToast('Invalid mobile number', 'warning');
            return;
        }

        $.get(API_BASE_URL + `/volunteers/mobile/${mobile}`, function (res) {

            if (res.data && res.data.length > 0) {

                const volunteer = res.data[0];

                currentVolunteerId =
                    volunteer.volunteerId ||
                    volunteer.volunteer_id ||
                volunteer.Id;

                currentMobile = mobile;

                // Demo OTP
                const otp = volunteer.OTP;

                sessionStorage.setItem("login_otp", otp);
                sessionStorage.setItem("role", volunteer.Role);

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

    // ================= OTP AUTO MOVE =================
    $(document).on('input', '.otp-input', function () {

        if ($(this).val().length === 1) {

            $(this).next('.otp-input').focus();
        }

    });

    // ================= VERIFY OTP =================
    $('#verifyOtpBtn').click(function () {

        let enteredOtp = "";

        $('.otp-input').each(function () {

            enteredOtp += $(this).val();
        });

        if (enteredOtp.length !== 4) {

            showToast('Enter 4 digit OTP', 'warning');
            return;
        }

        const savedOtp = sessionStorage.getItem("login_otp");
        const role = sessionStorage.getItem("role");

        if (enteredOtp === savedOtp) {

            showToast('✅ Login successful', 'success');
            let redirectUrl = "";

            if (role === "volunteer") {

                redirectUrl = `../../templates/Volunteers/Assignments.html?volunteerid=${currentVolunteerId}`;

            }
            else if (role === "teamlead") {

                redirectUrl = `../../templates/TeamLead/TeamLeadDashboard.html?teamleadid=${currentVolunteerId}`;

            }
            else if (role === "pastor") {

                redirectUrl = `../../templates/Pastor/PastorDashboard.html`;

            }
            else {

                showToast('Unknown role. Contact admin', 'error');
                return;
            }

            setTimeout(() => {

                window.location.href = redirectUrl;

            }, 800);

        } else {

            showToast('Invalid OTP', 'error');
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

});