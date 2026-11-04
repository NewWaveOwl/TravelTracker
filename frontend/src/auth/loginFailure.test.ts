import { expect, it } from 'vitest';
import { loginFailure } from './loginFailure';
it('explains application authorization without exposing provider identifiers', () => {
  const text = loginFailure({ error: 'invalid_request', error_description: 'Client "private-id" is not authorized to access resource server "private-resource".' });
  expect(text).toContain('authorize its access');
  expect(text).not.toContain('private-');
});
it('explains missing API registration without rendering raw descriptions', () => {
  expect(loginFailure({ error_description: 'Service not found: private-resource' })).toContain('check the API settings');
});
it('never displays unknown provider messages', () => {
  expect(loginFailure(new Error('secret-token'))).toBe('Sign-in was canceled or could not be completed. You can try again.');
});
