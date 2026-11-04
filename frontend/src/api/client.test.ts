import { afterEach, describe, expect, it, vi } from 'vitest';
import { getJson } from './client';

afterEach(() => vi.unstubAllGlobals());
describe('public HTTP client', () => {
  it('reads public health JSON without credentials', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ status: 'Healthy' })));
    vi.stubGlobal('fetch', fetch);
    await expect(getJson('/health')).resolves.toEqual({ status: 'Healthy' });
    expect(fetch.mock.calls[0][1].headers).toEqual({ Accept: 'application/json' });
  });
  it.each([[400, 'validation'], [401, 'permission'], [403, 'permission'], [503, 'unavailable'], [404, 'unexpected']])('classifies HTTP %s as %s', async (status, kind) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: Number(status) })));
    await expect(getJson('/health')).rejects.toMatchObject({ kind, status });
  });
  it('reports network failure', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('offline')));
    await expect(getJson('/health')).rejects.toMatchObject({ kind: 'network' });
  });
  it('reports malformed responses', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('invalid')));
    await expect(getJson('/health')).rejects.toMatchObject({ kind: 'unexpected' });
  });
  it('preserves caller cancellation', async () => {
    const controller = new AbortController(); controller.abort();
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(controller.signal.reason));
    await expect(getJson('/health', controller.signal)).rejects.toBe(controller.signal.reason);
  });
});
