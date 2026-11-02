import { fileURLToPath } from 'node:url';
import swaggerJsdoc from 'swagger-jsdoc';

/**
 * The OpenAPI document of this service, served at /openapi.json. swagger-jsdoc builds it from this definition (the shared
 * parts: the schemas, the security) and the `@openapi` comments above the routes in server.js (the operations), so that the
 * description of an endpoint sits next to its code.
 */
export const openApiDocument = swaggerJsdoc({
  definition: {
    openapi: '3.0.3',
    info: {
      title: 'grots-menu',
      version: '1.0.0',
      description: [
        "The menu of Grot's Pizzeria: the pizzas, sides and desserts, what they cost, and the special offers that change every",
        '5 minutes. Every price says which currency it is in (`currency`), which is GBP unless the service is configured otherwise.',
        '',
        'Every endpoint except `/healthz` needs a JWT bearer token, signed with the key shared by the services (HS256, with the',
        'issuer `grots-web` and the audience `grots-pizzeria` unless configured otherwise). `grots-web` mints one for each call it makes.',
      ].join('\n'),
    },
    tags: [
      { name: 'Menu', description: 'What is on the menu, and what it costs now.' },
      { name: 'Health', description: 'Whether the service is running.' },
    ],
    security: [{ bearerAuth: [] }],
    components: {
      securitySchemes: {
        bearerAuth: {
          type: 'http',
          scheme: 'bearer',
          bearerFormat: 'JWT',
          description: 'A JWT signed (HS256) with the shared `JWT_SIGNING_KEY`, with the expected `iss` and `aud` claims.',
        },
      },
      schemas: {
        MenuOffer: {
          type: 'object',
          description: 'A special offer on a menu item, which lasts until `endsAt`.',
          required: ['discount', 'price', 'endsAt'],
          properties: {
            discount: { type: 'number', format: 'double', minimum: 0, maximum: 1, example: 0.15, description: 'The fraction taken off the list price: 0.15 is 15% off.' },
            price: { type: 'number', format: 'double', example: 8.08, description: 'The price with the offer applied, to the nearest penny.' },
            endsAt: { type: 'string', format: 'date-time', example: '2026-11-01T10:05:00.000Z', description: 'When the offer ends.' },
          },
        },
        MenuItem: {
          type: 'object',
          description: 'A pizza, side or dessert.',
          required: ['id', 'name', 'description', 'category', 'price', 'currency', 'vegetarian', 'popular', 'emoji', 'offer'],
          properties: {
            id: { type: 'string', example: 'margherita' },
            name: { type: 'string', example: 'Margherita' },
            description: { type: 'string', example: 'San Marzano tomatoes, fresh mozzarella and basil on a blistered crust.' },
            category: { type: 'string', enum: ['pizza', 'side', 'dessert'], example: 'pizza' },
            price: { type: 'number', format: 'double', example: 9.5, description: 'The list price, in the `currency`. It does not change when the item is on offer.' },
            currency: { type: 'string', pattern: '^[A-Z]{3}$', example: 'GBP', description: 'The ISO 4217 code of the currency of the prices of the item, and of its offer.' },
            vegetarian: { type: 'boolean' },
            popular: { type: 'boolean', description: 'Shown with a "Popular" badge.' },
            emoji: { type: 'string', example: '🍕' },
            offer: { allOf: [{ $ref: '#/components/schemas/MenuOffer' }], nullable: true, description: 'The special offer, or `null` when there is none.' },
          },
        },
        PriceRequest: {
          type: 'object',
          required: ['items'],
          properties: {
            items: {
              type: 'array',
              minItems: 1,
              items: {
                type: 'object',
                required: ['pizzaId', 'quantity'],
                properties: {
                  pizzaId: { type: 'string', example: 'margherita', description: 'The id of any menu item: a pizza, a side or a dessert.' },
                  quantity: { type: 'integer', minimum: 1, maximum: 20, example: 2 },
                },
              },
            },
          },
        },
        PriceLine: {
          type: 'object',
          required: ['pizzaId', 'name', 'quantity', 'unitPrice', 'lineTotal'],
          properties: {
            pizzaId: { type: 'string', example: 'margherita' },
            name: { type: 'string', example: 'Margherita' },
            quantity: { type: 'integer', example: 2 },
            unitPrice: { type: 'number', format: 'double', example: 9.5, description: 'What one costs now, in the `currency` of the quote: the offer price if the item is on offer.' },
            lineTotal: { type: 'number', format: 'double', example: 19 },
          },
        },
        PriceQuote: {
          type: 'object',
          required: ['lines', 'subtotal', 'currency'],
          properties: {
            lines: { type: 'array', items: { $ref: '#/components/schemas/PriceLine' } },
            subtotal: { type: 'number', format: 'double', example: 19, description: 'The sum of the lines, before tax, in the `currency`.' },
            currency: { type: 'string', pattern: '^[A-Z]{3}$', example: 'GBP', description: 'The ISO 4217 code of the currency of the amounts.' },
          },
        },
        Problem: {
          type: 'object',
          description: 'What every error response looks like.',
          required: ['title', 'status', 'detail'],
          properties: {
            title: { type: 'string', example: 'Not Found' },
            status: { type: 'integer', example: 404 },
            detail: { type: 'string', example: "There is no pizza with id 'unknown'." },
          },
        },
        Health: {
          type: 'object',
          required: ['status'],
          properties: { status: { type: 'string', example: 'ok' } },
        },
      },
      responses: {
        Unauthorized: {
          description: 'There is no valid bearer token.',
          headers: { 'WWW-Authenticate': { schema: { type: 'string', example: 'Bearer' } } },
          content: { 'application/json': { schema: { $ref: '#/components/schemas/Problem' } } },
        },
      },
    },
  },
  apis: [fileURLToPath(new URL('./server.js', import.meta.url))],
});
