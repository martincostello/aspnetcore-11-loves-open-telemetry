window.grotsFormat = {
    dateTime(iso) {
        const date = new Date(iso);

        if (Number.isNaN(date.getTime())) {
            return iso;
        }

        // `undefined` means "use the browser's locale"; the time zone defaults to the browser's too.
        return new Intl.DateTimeFormat(undefined, { dateStyle: 'short', timeStyle: 'short' }).format(date);
    },

    // An amount of money in the browser's locale: (9.5, 'GBP') -> "£9.50", or "9,50 £" where that is how it is written.
    // The currency is an ISO 4217 code that comes from the API; one that Intl does not know is shown as it is.
    currency(amount, currency) {
        try {
            // Always 2 decimal places, as the API's amounts have: Intl would show a currency such as the yen with none, rounding
            // 9.50 to 10 when 9.50 is what is charged.
            return new Intl.NumberFormat(undefined, { style: 'currency', currency, minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(amount);
        } catch {
            return `${amount.toFixed(2)} ${currency}`;
        }
    },

    // A fraction as a percentage in the browser's locale: 0.08 -> "8%".
    percent(fraction) {
        return new Intl.NumberFormat(undefined, { style: 'percent', maximumFractionDigits: 2 }).format(fraction);
    },

    // The full date and time, to the second, for tooltips.
    dateTimeLong(iso) {
        const date = new Date(iso);

        if (Number.isNaN(date.getTime())) {
            return iso;
        }

        return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'medium' }).format(date);
    },

    relative(iso) {
        const seconds = Math.round((new Date(iso).getTime() - Date.now()) / 1000);

        if (Number.isNaN(seconds)) {
            return iso;
        }

        if (Math.abs(seconds) < 30) {
            return 'Just now';
        }

        if (seconds <= -30 && seconds > -60) {
            return 'Less than a minute ago';
        }

        const units = [['day', 86400], ['hour', 3600], ['minute', 60], ['second', 1]];
        const [unit, size] = units.find(([, size]) => Math.abs(seconds) >= size) ?? ['second', 1];

        return new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' }).format(Math.trunc(seconds / size), unit);
    },
};
