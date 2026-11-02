import { test } from 'node:test';
import assert from 'node:assert/strict';
import { pizzas } from '../src/menu.js';
import { OFFER_DISCOUNT, OFFER_PERIOD_MS, currentOffers, offerPrice, parseDiscount } from '../src/offers.js';

const at = (iso) => Date.parse(iso);
const byId = new Map(pizzas.map((p) => [p.id, p]));
const categoryOf = (id) => byId.get(id).category;

test('puts one pizza, one side and one dessert on offer', () => {
  const offers = currentOffers(at('2026-11-01T10:00:00Z'));

  assert.equal(offers.size, 3);
  assert.deepEqual([...offers.keys()].map(categoryOf).sort(), ['dessert', 'pizza', 'side']);
});

test('takes 15% off by default, rounded to the penny', () => {
  assert.equal(OFFER_DISCOUNT, 0.15);
  for (const offer of currentOffers(at('2026-11-01T10:00:00Z')).values()) {
    assert.equal(offer.discount, 0.15);
  }
  assert.equal(offerPrice(14), 11.9);
  assert.equal(offerPrice(8.5), 7.23); // 7.225
  assert.equal(offerPrice(3), 2.55);
  assert.equal(offerPrice(4.25), 3.61); // 3.6125
  assert.equal(offerPrice(13.75), 11.69); // 11.6875
});

test('takes whatever discount is configured off, and says what it is', () => {
  for (const offer of currentOffers(at('2026-11-01T10:00:00Z'), undefined, 0.2).values()) {
    assert.equal(offer.discount, 0.2);
  }
  assert.equal(offerPrice(10, 0.2), 8);
  assert.equal(offerPrice(9.5, 0.1), 8.55);
  assert.equal(offerPrice(9.5, 0.15), 8.08);
});

test('reads the discount from the configuration, which must be a fraction between 0 and 1', () => {
  assert.equal(parseDiscount(undefined), 0.15);
  assert.equal(parseDiscount(''), 0.15);
  assert.equal(parseDiscount(' 0.25 '), 0.25);

  for (const bad of ['15', '0', '1', '-0.1', 'half', 'NaN', 'Infinity']) {
    assert.throws(() => parseDiscount(bad), /OFFER_DISCOUNT/, bad);
  }
});

test('the offers last for 5 minutes of wall-clock time, whenever the service started', () => {
  const start = at('2026-11-01T10:05:00Z');
  const first = currentOffers(start);

  for (const offset of [0, 1, 60_000, 299_999]) {
    assert.deepEqual(currentOffers(start + offset), first, `at +${offset}ms`);
  }
  for (const offer of first.values()) {
    assert.equal(offer.endsAt, '2026-11-01T10:10:00.000Z');
  }

  // A moment before the period began, the previous period's offers (which end exactly when this one starts) are on
  for (const offer of currentOffers(start - 1).values()) {
    assert.equal(offer.endsAt, '2026-11-01T10:05:00.000Z');
  }
});

test('the choice is random from one period to the next, and any item can be chosen', () => {
  const start = at('2026-11-01T00:00:00Z');
  const chosen = new Set();
  const changes = { pizza: 0, side: 0, dessert: 0 };
  let previous;

  for (let i = 0; i < 2_000; i++) {
    const ids = [...currentOffers(start + i * OFFER_PERIOD_MS).keys()];
    ids.forEach((id) => chosen.add(id));

    if (previous) {
      for (const id of ids) {
        if (!previous.includes(id)) changes[categoryOf(id)]++;
      }
    }
    previous = ids;
  }

  assert.equal(chosen.size, pizzas.length, 'every item is chosen at some point');

  // Roughly 11 in 12 periods (5 in 6 for desserts) should pick a different item (the same one twice in a row is allowed, so not all do)
  for (const category of ['pizza', 'side', 'dessert']) {
    assert.ok(changes[category] > 1_500 && changes[category] < 1_999, `${category}: ${changes[category]} changes in 1999`);
  }
});

test('the two sections are chosen independently of each other', () => {
  const start = at('2026-11-01T00:00:00Z');
  const pairs = new Set();

  for (let i = 0; i < 2_000; i++) {
    pairs.add([...currentOffers(start + i * OFFER_PERIOD_MS).keys()].sort().join('+'));
  }

  assert.ok(pairs.size > 500, `${pairs.size} different pizza, side and dessert combinations`);
});
