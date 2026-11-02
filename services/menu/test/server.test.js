import { after, before, test } from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { SignJWT } from 'jose';

const port = 18081;
const base = `http://localhost:${port}`;
const signingKey = 'unit-test-signing-key-0123456789-abcdef';
const encode = (key) => new TextEncoder().encode(key);
let child;

// Mint a token like grots-web does; overrides allow tests to produce invalid ones.
const token = ({ key = signingKey, audience = 'grots-pizzeria', issuer = 'grots-web', expiresIn = '5m' } = {}) =>
  new SignJWT({ name: 'grot' })
    .setProtectedHeader({ alg: 'HS256' })
    .setSubject('42')
    .setIssuer(issuer)
    .setAudience(audience)
    .setIssuedAt()
    .setExpirationTime(expiresIn)
    .sign(encode(key));

const authed = async (options) => ({ authorization: `Bearer ${await token(options)}` });

before(async () => {
  child = spawn(process.execPath, ['src/server.js'], {
    env: { ...process.env, PORT: String(port), JWT_SIGNING_KEY: signingKey },
    stdio: 'ignore',
  });
  for (let i = 0; i < 50; i++) {
    try {
      if ((await fetch(`${base}/healthz`)).ok) return;
    } catch {
      await new Promise((r) => setTimeout(r, 100));
    }
  }
  throw new Error('Server did not start.');
});

after(() => child.kill());

const get = async (path, options) => fetch(`${base}${path}`, { headers: await authed(options) });
const post = async (body) =>
  fetch(`${base}/menu/price`, { method: 'POST', headers: { 'content-type': 'application/json', ...(await authed()) }, body });

test('health check is anonymous', async () => {
  assert.equal((await fetch(`${base}/healthz`)).status, 200);
});

test('serves the API documentation page at the root, without a token', async () => {
  for (const path of ['/', '/swagger-ui/theme.css', '/swagger-ui/init.js']) {
    const response = await fetch(`${base}${path}`);
    assert.equal(response.status, 200, path);
  }

  const page = await (await fetch(`${base}/`)).text();
  assert.match(page, /<title>grots-menu API/);
  assert.match(page, /data-spec-url="\/openapi\.json"/);
});

test('describes the API in an OpenAPI document, without a token', async () => {
  const response = await fetch(`${base}/openapi.json`);
  assert.equal(response.status, 200);
  assert.match(response.headers.get('content-type'), /application\/json/);

  const document = await response.json();
  assert.match(document.openapi, /^3\./);
  assert.equal(document.info.title, 'grots-menu');
  assert.deepEqual(Object.keys(document.paths).sort(), ['/healthz', '/menu', '/menu/price', '/menu/{id}']);
  assert.equal(document.components.securitySchemes.bearerAuth.scheme, 'bearer');
  assert.deepEqual(document.paths['/healthz'].get.security, []);
  assert.ok(document.components.schemas.MenuItem.properties.offer);
});

test('says what currency the prices are in, for every item and every quote', async () => {
  const menu = await (await get('/menu')).json();
  assert.ok(menu.every((item) => item.currency === 'GBP'));
  assert.equal((await (await get('/menu/margherita')).json()).currency, 'GBP');

  const quote = await (await post(JSON.stringify({ items: [{ pizzaId: 'margherita', quantity: 1 }] }))).json();
  assert.equal(quote.currency, 'GBP');
});

test('lists the menu', async () => {
  const items = await (await get('/menu')).json();
  assert.equal(items.length, 30); // 12 pizzas, 12 sides and 6 desserts
});

test('gets a pizza and 404s for an unknown one', async () => {
  assert.equal((await get('/menu/margherita')).status, 200);
  assert.equal((await get('/menu/nope')).status, 404);
});


// The offers change every 5 minutes: if one of these tests straddles that moment, the first try can legitimately fail, so try again.
const withStableOffers = async (check) => {
  for (let attempt = 1; ; attempt++) {
    const items = await (await get('/menu')).json();
    const endsAt = Date.parse(items.find((i) => i.offer).offer.endsAt);
    if (endsAt - Date.now() > 3_000) {
      try {
        return await check(items);
      } catch (error) {
        if (Date.now() < endsAt || attempt > 2) throw error;
      }
    }
    await new Promise((r) => setTimeout(r, 1_000));
  }
};

test('prices an order', async () => {
  // Margherita costs 9.50, unless it happens to be on special offer right now
  await withStableOffers(async (items) => {
    const margherita = items.find((i) => i.id === 'margherita');
    const res = await post(JSON.stringify({ items: [{ pizzaId: 'margherita', quantity: 2 }] }));
    assert.equal(res.status, 200);
    assert.deepEqual((await res.json()).subtotal, Math.round((margherita.offer?.price ?? 9.5) * 2 * 100) / 100);
  });
});

test('rejects invalid price requests', async () => {
  assert.equal((await post(JSON.stringify({ items: [] }))).status, 400);
  assert.equal((await post(JSON.stringify({ items: [{ pizzaId: 'margherita', quantity: 21 }] }))).status, 400);
  assert.equal((await post(JSON.stringify({ items: [{ pizzaId: 'nope', quantity: 1 }] }))).status, 400);
  assert.equal((await post('{not json')).status, 400);
});

test('never prices anything below zero', async () => {
  // Every way of asking for a quantity that is not a whole number from 1 to 20 is refused, including ones that would
  // cancel out another line (so a quote can never be less than the sum of what was asked for)
  for (const quantity of [-1, -20, 0, 21, 1.5, -0.5, '2', '-2', null, true, [1], {}, Number.MAX_SAFE_INTEGER, undefined]) {
    const res = await post(JSON.stringify({ items: [{ pizzaId: 'margherita', quantity }] }));
    assert.equal(res.status, 400, `quantity ${JSON.stringify(quantity)}`);
  }

  const cancelOut = await post(JSON.stringify({ items: [{ pizzaId: 'grot-special', quantity: 5 }, { pizzaId: 'grot-special', quantity: -5 }] }));
  assert.equal(cancelOut.status, 400);

  // And what is priced is never negative or free
  const items = await (await get('/menu')).json();
  assert.ok(items.every((i) => i.price > 0 && (i.offer === null || i.offer.price > 0)));
});

test('rejects requests without a valid token', async () => {
  const bad = [
    {},
    { authorization: 'Bearer not-a-jwt' },
    await authed({ key: 'a-different-signing-key-0123456789-abcdef' }),
    await authed({ audience: 'someone-else' }),
    await authed({ issuer: 'someone-else' }),
    await authed({ expiresIn: '-5m' }),
  ];
  for (const headers of bad) {
    for (const path of ['/menu', '/menu/margherita']) {
      const res = await fetch(`${base}${path}`, { headers });
      assert.equal(res.status, 401);
      assert.equal((await res.json()).title, 'Unauthorized');
    }
  }
  const unauthenticatedPost = await fetch(`${base}/menu/price`, { method: 'POST', body: '{not json' });
  assert.equal(unauthenticatedPost.status, 401);
});

test('lists each category from cheapest to dearest, with the Grot Special as the dearest pizza', async () => {
  const items = await (await get('/menu')).json();

  for (const category of ['pizza', 'side', 'dessert']) {
    const prices = items.filter((i) => i.category === category).map((i) => i.price);
    assert.deepEqual(prices, [...prices].sort((a, b) => a - b), `${category} prices should not decrease`);
  }

  const pizzas = items.filter((i) => i.category === 'pizza');
  assert.equal(pizzas.at(-1).id, 'grot-special');
  assert.ok(pizzas.at(-1).price > Math.max(...pizzas.slice(0, -1).map((p) => p.price)));
});

test('puts one pizza, one side and one dessert on special offer, 15% off', async () => {
  await withStableOffers(async (items) => {
    const offered = items.filter((i) => i.offer);
    assert.deepEqual(offered.map((i) => i.category).sort(), ['dessert', 'pizza', 'side']);

    for (const item of offered) {
      assert.equal(item.offer.discount, 0.15);
      assert.equal(item.offer.price, Math.round(item.price * 85) / 100);
      assert.ok(item.price > item.offer.price, 'the list price is still the list price');

      const endsAt = Date.parse(item.offer.endsAt);
      assert.equal(endsAt % (5 * 60 * 1000), 0, 'periods are aligned to the wall clock');
      assert.ok(endsAt > Date.now() && endsAt - Date.now() <= 5 * 60 * 1000);
    }

    assert.ok(items.filter((i) => !i.offer).every((i) => i.offer === null), 'items that are not on offer say so');
  });
});

test('shows the offer when getting one item', async () => {
  await withStableOffers(async (items) => {
    const offered = items.find((i) => i.offer);
    assert.deepEqual((await (await get(`/menu/${offered.id}`)).json()).offer, offered.offer);
  });
});

test('prices items on offer at the offer price', async () => {
  await withStableOffers(async (items) => {
    const offered = items.find((i) => i.offer);
    const regular = items.find((i) => !i.offer);
    const res = await post(JSON.stringify({ items: [{ pizzaId: offered.id, quantity: 3 }, { pizzaId: regular.id, quantity: 1 }] }));
    const quote = await res.json();

    assert.equal(quote.lines[0].unitPrice, offered.offer.price);
    assert.equal(quote.lines[0].lineTotal, Math.round(offered.offer.price * 3 * 100) / 100);
    assert.equal(quote.lines[1].unitPrice, regular.price);
  });
});

test('has six desserts', async () => {
  const items = await (await get('/menu')).json();
  const desserts = items.filter((i) => i.category === 'dessert');

  assert.deepEqual(desserts.map((i) => i.id), [
    'lemon-sorbet', 'vanilla-gelato', 'chocolate-brownie', 'affogato', 'strawberry-cheesecake', 'tiramisu',
  ]);
  assert.ok(desserts.every((i) => i.vegetarian));
});

test('marks the popular items', async () => {
  const items = await (await get('/menu')).json();
  const popular = items.filter((i) => i.popular).map((i) => i.id).sort();

  assert.deepEqual(popular, [
    'chicken-wings', 'garlic-bread', 'grot-special', 'marinated-olives', 'pepperoni',
    'quattro-formaggi', 'side-salad', 'sweet-potato-fries', 'tiramisu', 'vanilla-gelato', 'veggie-supreme',
  ]);
  assert.ok(items.every((i) => typeof i.popular === 'boolean'));
});
