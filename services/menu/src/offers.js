import { pizzas } from './menu.js';

/** Special offers last for 5 minutes of wall-clock time: 10:00:00 to 10:04:59.999, then 10:05:00 to 10:09:59.999, and so on. */
export const OFFER_PERIOD_MS = 5 * 60 * 1000;

export const OFFER_PERCENT_OFF = 15;

/** Setting SPECIAL_OFFERS=off switches the offers off (the prices are then always the list prices), which keeps tests that check totals deterministic. */
export const offersEnabled = () => (process.env.SPECIAL_OFFERS ?? 'on').trim().toLowerCase() !== 'off';

// A well-mixed 32-bit hash (the finalizer of MurmurHash3), so that consecutive periods give unrelated picks.
const hash32 = (value) => {
  let h = Math.imul(value ^ (value >>> 16), 0x85ebca6b);
  h = Math.imul(h ^ (h >>> 13), 0xc2b2ae35);
  return (h ^ (h >>> 16)) >>> 0;
};

/** The price with the offer applied, in pounds: the list price less 15%, to the nearest penny. */
export const offerPrice = (price) => Math.round((Math.round(price * 100) * (100 - OFFER_PERCENT_OFF)) / 100) / 100;

/**
 * The items on special offer at the given time (Date.now() by default): one item from each section of the menu (pizzas and
 * sides and desserts), chosen at random for each 5 minute period. Nothing is stored: the choice is a function of the period, so every
 * instance of the service agrees without talking to the others, and the same items may well be picked twice in a row.
 *
 * @returns {Map<string, { discount: number, price: number, endsAt: string }>} by item id
 */
export function currentOffers(now = Date.now(), items = pizzas) {
  const period = Math.floor(now / OFFER_PERIOD_MS);
  const endsAt = new Date((period + 1) * OFFER_PERIOD_MS).toISOString();
  const categories = [...new Set(items.map((item) => item.category))].sort();
  const offers = new Map();

  categories.forEach((category, index) => {
    const candidates = items.filter((item) => item.category === category);
    const chosen = candidates[hash32(hash32(period) + index + 1) % candidates.length];
    offers.set(chosen.id, { discount: OFFER_PERCENT_OFF / 100, price: offerPrice(chosen.price), endsAt });
  });

  return offers;
}

/** The offers to apply right now: none when they are switched off. */
export const activeOffers = () => (offersEnabled() ? currentOffers() : new Map());

/** The item as the API shows it: with its offer, or `offer: null`. `price` stays the list price. */
export const withOffer = (item, offers) => ({ ...item, offer: offers.get(item.id) ?? null });

/** What one of the item costs now. */
export const currentPrice = (item, offers) => offers.get(item.id)?.price ?? item.price;
