import { jwtVerify } from 'jose';
import { log } from './logger.js';

// Service-to-service security: every endpoint except /healthz requires an
// HS256 JWT bearer token minted by grots-web. See docs/service-contract.md.
const signingKey = process.env.JWT_SIGNING_KEY ?? '';
const issuer = process.env.JWT_ISSUER || 'grots-web';
const audience = process.env.JWT_AUDIENCE || 'grots-pizzeria';

if (Buffer.byteLength(signingKey, 'utf8') < 32) {
  console.error('JWT_SIGNING_KEY must be set to at least 32 bytes of text.');
  process.exit(1);
}

const key = new TextEncoder().encode(signingKey);

// Registered on each route (rather than globally) so that a rejected request is
// still reported with the route it was aimed at (e.g. http.route=/menu/:id).
export async function requireAuth(req, res, next) {
  const header = req.get('authorization') ?? '';
  const [scheme, token] = header.split(' ');

  let reason = 'missing or malformed Authorization header';
  if (scheme?.toLowerCase() === 'bearer' && token) {
    try {
      const { payload } = await jwtVerify(token, key, {
        algorithms: ['HS256'],
        issuer,
        audience,
        clockTolerance: 30,
      });
      req.auth = payload;
      return next();
    } catch (err) {
      reason = err.code ?? 'invalid token';
    }
  }

  log.warn('Rejected request with invalid credentials', { 'grot.auth.failure': reason });
  res
    .status(401)
    .set('WWW-Authenticate', 'Bearer')
    .json({ title: 'Unauthorized', status: 401, detail: 'A valid bearer token is required.' });
}
