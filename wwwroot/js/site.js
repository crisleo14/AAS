// Please see documentation at https://docs.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

$(document).ready(function () {
    $('.js-multiple').select2({
        placeholder: "Select an option",
        allowClear: true,
        width: 'resolve'
    });
});

$(document).ready(function () {
    $('.js-select2').select2({
        placeholder: "Select an option",
        allowClear: true,
        width: 'resolve',
        theme: 'classic'
    });
});

// hack to fix jquery 3.6 focus security patch that bugs auto search in select-2
$(document).on('select2:open', () => {
    document.querySelector('.select2-search__field').focus();
});

$(document).ready(function () {
    $('#dataTable').DataTable({
        stateSave: true,
    });
});

//Money Input formatting
$(document).ready(function () {
    // Store the initial input value for each moneyInput field
    $(".moneyInput").each(function () {
        $(this).data("initialValue", $(this).val());
    });

    // Attach a focus event listener to the moneyInput fields
    $(".moneyInput").on("focus", function () {
        // Get the initial value
        var initialValue = $(this).data("initialValue");

        // If the current value is the initial value, clear it
        if (initialValue === '0.00' || initialValue === '0') {
            $(this).val('');
        }
    });

    // Attach a blur event listener to the moneyInput fields
    $(".moneyInput").on("blur", function () {
        // Get the initial value
        var initialValue = $(this).data("initialValue");

        // If the current value is empty or only whitespace, set it back to the initial value
        if (!$(this).val().trim()) {
            $(this).val(initialValue);
        }
    });

    // Attach an input event listener to the moneyInput field
    $(".moneyInput").on("input", function () {
        // Get the current value of the input
        var inputValue = $(this).val();

        // If the input starts with a dot, add '0' before it
        if (inputValue.charAt(0) === '.') {
            inputValue = '0' + inputValue;
        }

        // Remove non-numeric characters except for the decimal point
        var numericValue = inputValue.replace(/[^\d.]/g, '');

        // Format the numeric value with commas and two decimal places
        var formattedValue = numberWithCommasAndDecimals(numericValue);

        // Set the formatted value back to the input field
        $(this).val(formattedValue);
    });

    // Function to add commas and two decimal places to a number
    function numberWithCommasAndDecimals(number) {
        // Split the number into integer and decimal parts
        var parts = number.split('.');
        var integerPart = parts[0];
        var decimalPart = parts.length > 1 ? '.' + parts[1] : '';

        // Add commas to the integer part
        var numberWithCommas = integerPart.toString().replace(/\B(?=(\d{3})+(?!\d))/g, ",");

        // Combine the integer and decimal parts
        return numberWithCommas + decimalPart;
    }
});

// Dynamic date to in books
document.addEventListener('DOMContentLoaded', function () {
    var dateFromInput = document.getElementById('DateFrom');
    var dateToInput = document.getElementById('DateTo');

    // Add an event listener to DateFrom input
    dateFromInput?.addEventListener('change', function () {
        // Set DateTo input value to DateFrom input value
        dateToInput.value = dateFromInput.value;
    });
});

document.addEventListener('DOMContentLoaded', function () {
    function formatNumberWithCommas(value) {
        const [integerPart, decimalPart] = value.toString().split('.');
        const formattedIntegerPart = integerPart.replace(/\B(?=(\d{3})+(?!\d))/g, ",");
        return decimalPart ? `${formattedIntegerPart}.${decimalPart}` : formattedIntegerPart;
    }

    function removeCommas(value) {
        return value.replace(/,/g, '');
    }

    function handleFocus(event) {
        event.target.value = removeCommas(event.target.value);
    }

    function handleBlur(event) {
        const inputValue = parseFloat(removeCommas(event.target.value));
        if (isNaN(inputValue) || inputValue <= 0) {
            event.target.classList.add('error');
            event.target.nextElementSibling.textContent = 'Value must be greater than zero.';
        } else {
            event.target.classList.remove('error');
            event.target.nextElementSibling.textContent = '';
            event.target.value = formatNumberWithCommas(event.target.value);
        }
    }

    function handleSubmit(event) {
        document.querySelectorAll('.money').forEach(function (element) {
            element.value = removeCommas(element.value);
        });
    }

    document.querySelectorAll('.money').forEach(function (element) {
        element.addEventListener('focus', handleFocus);
        element.addEventListener('blur', handleBlur);
    });

    document.querySelector('form').addEventListener('submit', handleSubmit);
});


function setTransactionDate() {
    // Get the current date in the format "YYYY-MM-DD" (required for the date input)
    var currentDate = new Date().toISOString().slice(0, 10);

    var transactionDateField = document.getElementById("TransactionDate");

    if (transactionDateField.value == '0001-01-01') {
        transactionDateField.value = currentDate;
    }
}

function formatNumber(number) {
    return number.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function formatNumberToFour(number) {
    return number.toLocaleString('en-US', { minimumFractionDigits: 4, maximumFractionDigits: 4 });
}

function parseNumber(formattedNum) {
    return parseFloat(formattedNum.replace(/,/g, '')) || 0;
}

// start code for formatting of input type for tin number
document.addEventListener('DOMContentLoaded', () => {
    const inputFields = document.querySelectorAll('.formattedTinNumberInput');

    inputFields.forEach(inputField => {
        inputField.addEventListener('input', (e) => {
            let value = e.target.value.replace(/-/g, ''); // Remove existing dashes
            let formattedValue = '';

            // Add dashes after every 3 digits, keeping the last 5 digits without dashes
            for (let i = 0; i < value.length; i++) {
                if (i === 3 || i === 6 || i === 9) {
                    formattedValue += '-';
                }
                formattedValue += value[i];
            }

            // If there are more than 12 characters, don't add a dash after the 10th character (i.e., for the last 5 digits)
            if (formattedValue.length > 12) {
                formattedValue = formattedValue.substring(0, 12) + formattedValue.substring(12).replace(/-/g, '');
            }

            e.target.value = formattedValue;
        });

        inputField.addEventListener('keydown', (e) => {
            if (e.key === 'Backspace') {
                let value = e.target.value;
                // Remove the dash when backspace is pressed if it is at the end of a section of 3 digits
                if (value.endsWith('-')) {
                    e.target.value = value.slice(0, -1);
                }
            }
        });
    });
});
// end of code for formatting of input type for tin number

//navigation bar dropend implementation
document.addEventListener("DOMContentLoaded", function () {
    // Get all dropend elements
    const dropends = document.querySelectorAll(".dropend");

    // Track the currently open parent dropend
    let openParentDropend = null;

    dropends.forEach(function (dropend) {
        dropend.addEventListener("click", function (event) {
            // Stop event from bubbling up
            event.stopPropagation();

            const clickedMenu = this.querySelector(".dropdown-menu");

            // If clicking on a child menu inside an open parent, allow it
            if (openParentDropend && openParentDropend.contains(this)) {
                return;
            }

            // Close the currently open parent dropend if different
            if (openParentDropend && openParentDropend !== this) {
                const openMenu = openParentDropend.querySelector(".dropdown-menu");
                if (openMenu) {
                    openMenu.classList.remove("show");
                }
            }

            // Open the clicked dropend
            if (clickedMenu) {
                clickedMenu.classList.add("show");
                openParentDropend = this;
            }
        });
    });
});