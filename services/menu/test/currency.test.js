import { test } from 'node:test';
import assert from 'node:assert/strict';
import { CURRENCY, parseCurrency } from '../src/currency.js';

test('prices are in pounds sterling unless the service is told otherwise', () => {
  assert.equal(CURRENCY, 'GBP');
  assert.equal(parseCurrency(undefined), 'GBP');
  assert.equal(parseCurrency(''), 'GBP');
  assert.equal(parseCurrency('  '), 'GBP');
});

test('takes the currency from the configuration, which must be an ISO 4217 code', () => {
  assert.equal(parseCurrency('USD'), 'USD');
  assert.equal(parseCurrency(' EUR '), 'EUR');

  for (const bad of ['gbp', 'GB', 'GBPP', 'pounds', '£', '12A']) {
    assert.throws(() => parseCurrency(bad), /CURRENCY/, bad);
  }
});
