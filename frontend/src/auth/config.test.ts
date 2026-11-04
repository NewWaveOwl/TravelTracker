import { expect, it } from 'vitest';
import { readAuthConfig } from './config';
it('keeps unconfigured sign-in disabled', () => expect(readAuthConfig({})).toBeNull());
it('reads explicit audience and least privilege defaults', () => {
  expect(readAuthConfig({ VITE_AUTH0_DOMAIN: 'tenant.auth0.com', VITE_AUTH0_CLIENT_ID: 'public-id', VITE_AUTH0_AUDIENCE: 'traveltracker-api' })).toEqual({ domain: 'tenant.auth0.com', clientId: 'public-id', audience: 'traveltracker-api', scope: 'openid profile email catalogue.read' });
});
it.each(['http://tenant.auth0.com', 'tenant.auth0.com/path', 'user@tenant.auth0.com', 'localhost'])('rejects malformed domain %s', domain => {
  expect(readAuthConfig({ VITE_AUTH0_DOMAIN: domain, VITE_AUTH0_CLIENT_ID: 'id', VITE_AUTH0_AUDIENCE: 'api' })).toBeNull();
});
