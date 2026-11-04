// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest';
import { protectedGet } from './protectedRequest';

afterEach(() => vi.unstubAllGlobals());
const makeSession = () => ({ getToken: vi.fn().mockResolvedValue('test-access-token'), isActive: vi.fn().mockReturnValue(true), expire: vi.fn() });
it('attaches an access token to the chosen resource and refuses redirects', async () => {
  const fetch = vi.fn().mockResolvedValue(new Response('[]'));
  vi.stubGlobal('fetch', fetch);
  await expect(protectedGet('Attractions', '/api/v1/attractions', makeSession())).resolves.toEqual([]);
  expect(fetch.mock.calls[0][0]).toBe('http://localhost:3000/services/attractions/api/v1/attractions');
  expect(fetch.mock.calls[0][1]).toMatchObject({ headers: { Authorization: 'Bearer test-access-token' }, redirect: 'error', credentials: 'omit' });
});
it('does not acquire tokens or send requests while signed out', async () => {
  const session = makeSession(); session.isActive.mockReturnValue(false);
  const fetch = vi.fn(); vi.stubGlobal('fetch', fetch);
  await expect(protectedGet('Attractions', '/api/v1/attractions', session)).rejects.toMatchObject({ status: 401 });
  expect(session.getToken).not.toHaveBeenCalled(); expect(fetch).not.toHaveBeenCalled();
});
it('does not send a token acquired after logout', async () => {
  const session = makeSession(); session.isActive.mockReturnValueOnce(true).mockReturnValue(false);
  const fetch = vi.fn(); vi.stubGlobal('fetch', fetch);
  await expect(protectedGet('Attractions', '/api/v1/attractions', session)).rejects.toMatchObject({ status: 401 });
  expect(fetch).not.toHaveBeenCalled();
});
it('turns token renewal failure into a recoverable expired session', async () => {
  const session = makeSession(); session.getToken.mockRejectedValue(new Error('sensitive provider details'));
  await expect(protectedGet('Attractions', '/api/v1/attractions', session)).rejects.toMatchObject({ status: 401, message: 'Your session has expired. Sign in again.' });
  expect(session.expire).toHaveBeenCalledOnce();
});
it.each([401, 403])('handles API %s without retrying', async status => {
  const session = makeSession(); const fetch = vi.fn().mockResolvedValue(new Response('', { status })); vi.stubGlobal('fetch', fetch);
  await expect(protectedGet('Attractions', '/api/v1/attractions', session)).rejects.toMatchObject({ status });
  expect(session.expire).toHaveBeenCalledTimes(status === 401 ? 1 : 0); expect(fetch).toHaveBeenCalledOnce();
});
it.each(['https://other.example/api', '//other.example/api', '/api/v1/../secret', '/api/v1/attractions?token=x'])('rejects untrusted request path %s', async path => {
  const session = makeSession(); await expect(protectedGet('Attractions', path, session)).rejects.toMatchObject({ kind: 'unexpected' });
  expect(session.getToken).not.toHaveBeenCalled();
});
it('does not send an acquired token when the caller cancels', async () => {
  const session = makeSession(); const controller = new AbortController();
  session.getToken.mockImplementation(async () => { controller.abort(); return 'test-access-token'; });
  const fetch = vi.fn(); vi.stubGlobal('fetch', fetch);
  await expect(protectedGet('Attractions', '/api/v1/attractions', session, controller.signal)).rejects.toBeDefined();
  expect(fetch).not.toHaveBeenCalled();
});
it('rejects a response parsed after logout', async () => {
  const session = makeSession();
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => { session.isActive.mockReturnValue(false); return []; } }));
  await expect(protectedGet('Attractions', '/api/v1/attractions', session)).rejects.toMatchObject({ status: 401 });
});
