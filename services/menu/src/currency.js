const DEFAULT_CURRENCY = 'GBP';

/**
 * The currency the prices of the menu are in, as an ISO 4217 code (GBP, USD, EUR, ...): CURRENCY, or GBP if it is not set (a
 * blank value, as Docker Compose passes for an unset variable, is not set). It is sent with every menu item and every quote, so
 * that nothing else needs to assume one. Prices have 2 decimal places, whatever the currency. A value that is not a 3 letter
 * code, in capitals, stops the service starting.
 *
 * @param {string | undefined} value
 */
export function parseCurrency(value) {
  const text = (value ?? '').trim();

  if (text === '') {
    return DEFAULT_CURRENCY;
  }

  if (!/^[A-Z]{3}$/.test(text)) {
    throw new Error(`CURRENCY must be a 3 letter ISO 4217 code in capitals, such as GBP, but was '${text}'.`);
  }

  return text;
}

export const CURRENCY = parseCurrency(process.env.CURRENCY);
