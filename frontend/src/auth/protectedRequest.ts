import { ApiError, serviceUrls } from '../api/client';

export type AccessSession = {
  getToken: () => Promise<string>;
  isActive: () => boolean;
  expire: () => void;
};

/**
 * Sends an access token only to a configured service, with no redirect following.
 * @param service The explicitly configured resource server.
 * @param path A relative versioned API path.
 * @param session Current authentication access and expiry handler.
 * @param signal Cancels a pending request on logout or navigation.
 * @return Public JSON response, or a categorized error without token details.
 */
export async function protectedGet<T>(service: keyof typeof serviceUrls, path: string, session: AccessSession, signal?: AbortSignal): Promise<T> {
  if (!/^\/api\/v1\/[a-z0-9/-]+$/i.test(path)) throw new ApiError('unexpected', 'Unsupported API path.');
  const base = new URL(serviceUrls[service], window.location.origin);
  if (base.protocol !== 'https:' && !(base.protocol === 'http:' && ['localhost', '127.0.0.1', '[::1]'].includes(base.hostname))) throw new ApiError('unexpected', 'The API requires HTTPS.');
  if (base.username || base.password || base.search || base.hash) throw new ApiError('unexpected', 'Invalid service configuration.');
  if (!session.isActive()) throw new ApiError('permission', 'Sign in to continue.', 401);
  let token: string;
  try { token = await session.getToken(); }
  catch {
    if (signal?.aborted) throw signal.reason;
    session.expire();
    throw new ApiError('permission', 'Your session has expired. Sign in again.', 401);
  }
  signal?.throwIfAborted();
  if (!session.isActive()) throw new ApiError('permission', 'Sign in to continue.', 401);
  if (!token) throw new ApiError('permission', 'Sign in to continue.', 401);
  let response: Response;
  try {
    response = await fetch(`${base.href.replace(/\/$/, '')}${path}`, {
      headers: { Accept: 'application/json', Authorization: `Bearer ${token}` },
      redirect: 'error', credentials: 'omit',
      signal: signal ? AbortSignal.any([signal, AbortSignal.timeout(8000)]) : AbortSignal.timeout(8000),
    });
  } catch {
    if (signal?.aborted) throw signal.reason;
    throw new ApiError('network', 'Unable to connect. Please try again.');
  }
  signal?.throwIfAborted();
  if (!session.isActive()) throw new ApiError('permission', 'Sign in to continue.', 401);
  if (response.status === 401) {
    session.expire();
    throw new ApiError('permission', 'Your session was rejected. Sign in again.', 401);
  }
  if (response.status === 403) throw new ApiError('permission', 'You do not have permission for this action.', 403);
  if (!response.ok) throw new ApiError(response.status >= 500 ? 'unavailable' : 'unexpected', 'The service could not complete the request.', response.status);
  let data: T;
  try { data = await response.json() as T; }
  catch { throw new ApiError('unexpected', 'The service returned an unreadable response.'); }
  signal?.throwIfAborted();
  if (!session.isActive()) throw new ApiError('permission', 'Sign in to continue.', 401);
  return data;
}
