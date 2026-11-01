import express from 'express';
import { metrics } from '@opentelemetry/api';
import { findPizza, pizzas, round2 } from './menu.js';
import { activeOffers, currentPrice, withOffer } from './offers.js';
import { log } from './logger.js';
import { requireAuth } from './auth.js';

const port = Number.parseInt(process.env.PORT ?? '8081', 10);

const priceQuotes = metrics
  .getMeter('grots-menu')
  .createCounter('grot.menu.price_quotes', { unit: '{quote}', description: 'Number of price quotes computed.' });

class HttpError extends Error {
  constructor(status, title, detail) {
    super(detail);
    this.status = status;
    this.title = title;
  }
}

const app = express();
app.disable('x-powered-by');
const json = express.json();

app.get('/healthz', (_req, res) => {
  res.json({ status: 'ok' });
});

app.get('/menu', requireAuth, (_req, res) => {
  const offers = activeOffers();
  res.json(pizzas.map((pizza) => withOffer(pizza, offers)));
});

app.get('/menu/:id', requireAuth, (req, res) => {
  const pizza = findPizza(req.params.id);
  if (!pizza) {
    log.warn('Unknown pizza requested', { 'grot.pizza.id': req.params.id });
    throw new HttpError(404, 'Not Found', `There is no pizza with id '${req.params.id}'.`);
  }
  res.json(withOffer(pizza, activeOffers()));
});

app.post('/menu/price', requireAuth, json, (req, res) => {
  const items = req.body?.items;
  if (!Array.isArray(items) || items.length === 0) {
    throw new HttpError(400, 'Bad Request', "'items' must be a non-empty array.");
  }

  // One set of offers for the whole quote, so a quote never straddles the moment the offers change
  const offers = activeOffers();

  const lines = items.map((item) => {
    const quantity = item?.quantity;
    if (!Number.isInteger(quantity) || quantity < 1 || quantity > 20) {
      throw new HttpError(400, 'Bad Request', "'quantity' must be an integer between 1 and 20.");
    }
    const pizza = findPizza(item.pizzaId);
    if (!pizza) {
      log.warn('Price requested for unknown pizza', { 'grot.pizza.id': String(item.pizzaId) });
      throw new HttpError(400, 'Bad Request', `Unknown pizza '${item.pizzaId}'.`);
    }
    const unitPrice = currentPrice(pizza, offers);
    return {
      pizzaId: pizza.id,
      name: pizza.name,
      quantity,
      unitPrice,
      lineTotal: round2(unitPrice * quantity),
    };
  });

  const subtotal = round2(lines.reduce((sum, line) => sum + line.lineTotal, 0));
  priceQuotes.add(1);
  log.info('Price quote computed', { 'grot.quote.lines': lines.length, 'grot.quote.subtotal': subtotal });
  res.json({ lines, subtotal });
});

app.use((req, _res, next) => {
  next(new HttpError(404, 'Not Found', `No route for ${req.method} ${req.path}.`));
});

// eslint-disable-next-line no-unused-vars
app.use((err, _req, res, _next) => {
  // Errors thrown by body-parser (e.g. malformed JSON) carry a 4xx status.
  const status = err.status ?? err.statusCode ?? 500;
  const known = err instanceof HttpError;
  const title = known ? err.title : status === 400 ? 'Bad Request' : status >= 500 ? 'Internal Server Error' : 'Error';
  const detail = known ? err.message : status === 400 ? 'The request body is not valid JSON.' : 'An unexpected error occurred.';
  if (status >= 500) {
    log.error('Unhandled error', { 'exception.message': err.message, 'exception.stacktrace': err.stack });
  }
  res.status(status).json({ title, status, detail });
});

const server = app.listen(port, '0.0.0.0', () => {
  log.info(`grots-menu listening on port ${port}`);
});

for (const signal of ['SIGTERM', 'SIGINT']) {
  process.once(signal, () => server.close());
}
