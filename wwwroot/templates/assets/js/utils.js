// ====== MOBILE NUMBER VALIDATION ======
/**
 * Restricts an input field to accept only 10-digit mobile numbers
 * @param {string} inputId - The ID of the input element
 */
function restrictMobileInput(inputId) {
    const inputElement = document.getElementById(inputId);
    if (!inputElement) return;

    // Remove non-numeric characters on paste
    inputElement.addEventListener('paste', function (e) {
        e.preventDefault();
        const pastedText = (e.clipboardData || window.clipboardData).getData('text');
        const numericOnly = pastedText.replace(/\D/g, '');
        const first10Digits = numericOnly.substring(0, 10);
        inputElement.value = first10Digits;
        inputElement.dispatchEvent(new Event('input'));
    });

    // Allow only digits and enforce 10-digit limit
    inputElement.addEventListener('keypress', function (e) {
        if (!/[0-9]/.test(e.key)) {
            e.preventDefault();
        }
    });

    // Prevent typing beyond 10 digits
    inputElement.addEventListener('input', function (e) {
        let value = e.target.value.replace(/\D/g, '');
        e.target.value = value.substring(0, 10);
    });
}

/**
 * Restrict mobile input for multiple elements using class selector
 * @param {string} className - The class name of mobile input elements
 */
function restrictAllMobileInputs(className = 'mobile-input') {
    const mobileInputs = document.querySelectorAll(`.${className}`);
    mobileInputs.forEach((input) => {
        restrictMobileInput(input.id);
    });
}

/**
 * Validate if a mobile number has exactly 10 digits
 * @param {string} phoneNumber - The phone number to validate
 * @returns {boolean} - True if valid (10 digits), false otherwise
 */
function isValidMobileNumber(phoneNumber) {
    const cleaned = phoneNumber.replace(/\D/g, '');
    return cleaned.length === 10;
}

/**
 * Initialize mobile number restrictions on page load
 */
$(document).ready(function () {
    restrictAllMobileInputs('mobile-input');
});

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
