export type FailureKind = 'validation' | 'permission' | 'network' | 'unavailable' | 'unexpected';

export class ApiError extends Error {
  constructor(public kind: FailureKind, message: string, public status?: number) { super(message); }
}

/**
 * Reads a public JSON HTTP contract with bounded waiting and cancellation.
 * @param url Public service URL.
 * @param signal Cancels work when the consuming view closes.
 * @return Parsed public response; throws a categorized ApiError on failure.
 */
export async function getJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  let response: Response;
  try {
    response = await fetch(url, { signal: signal ? AbortSignal.any([signal, AbortSignal.timeout(8000)]) : AbortSignal.timeout(8000), headers: { Accept: 'application/json' } });
  } catch (error) {
    if (signal?.aborted) throw error;
    throw new ApiError('network', 'Check your connection and try again.');
  }
  if (!response.ok) {
    const kind = response.status === 400 || response.status === 422 ? 'validation' : response.status === 401 || response.status === 403 ? 'permission' : response.status >= 500 ? 'unavailable' : 'unexpected';
    throw new ApiError(kind, 'The request could not be completed.', response.status);
  }
  try { return await response.json() as T; }
  catch { throw new ApiError('unexpected', 'The service returned an unreadable response.'); }
}

export const serviceUrls = {
  Attractions: import.meta.env.VITE_ATTRACTIONS_URL || '/services/attractions',
  Transport: import.meta.env.VITE_TRANSPORT_URL || '/services/transport',
};
