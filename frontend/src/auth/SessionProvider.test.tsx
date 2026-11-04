// @vitest-environment jsdom
import { StrictMode, type ReactNode } from 'react';
import { afterEach, expect, it, vi } from 'vitest';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
const auth = vi.hoisted(() => ({ isAuthenticated: true, isLoading: false, error: undefined as Error | undefined, loginWithRedirect: vi.fn(), logout: vi.fn(), getAccessTokenSilently: vi.fn() }));
vi.mock('@auth0/auth0-react', () => ({ Auth0Provider: ({ children }: { children: ReactNode }) => children, useAuth0: () => auth }));
vi.mock('./config', () => ({ authConfig: { domain: 'tenant.auth0.com', clientId: 'public-id', audience: 'api', scope: 'openid catalogue.read' } }));
import SessionProvider from './SessionProvider';
import Account from '../components/Account';
afterEach(() => { cleanup(); vi.useRealTimers(); vi.restoreAllMocks(); sessionStorage.clear(); vi.clearAllMocks(); vi.unstubAllGlobals(); auth.error = undefined; auth.isAuthenticated = true; auth.isLoading = false; });
it('runs protected access in StrictMode and reports success', async () => {
  auth.getAccessTokenSilently.mockResolvedValue('test-access-token');
  const fetch = vi.fn().mockResolvedValue(new Response('{"name":"Example Bus"}'));
  vi.stubGlobal('fetch', fetch);
  render(<StrictMode><SessionProvider><Account /></SessionProvider></StrictMode>);
  fireEvent.click(screen.getByRole('button', { name: 'Check API access' }));
  expect(await screen.findByText('Your protected request completed successfully.')).toBeTruthy();
  expect(fetch.mock.calls[0][0]).toBe('http://localhost:3000/services/transport/api/v1/transport-modes/00000000-0000-0000-0000-000000000101');
});
it('removes protected controls immediately even if provider logout fails', async () => {
  auth.logout.mockRejectedValue(new Error('private diagnostics'));
  render(<SessionProvider><Account /></SessionProvider>);
  fireEvent.click(screen.getByRole('button', { name: 'Sign out' }));
  await waitFor(() => expect(screen.queryByRole('button', { name: 'Check API access' })).toBeNull());
  expect(await screen.findByRole('button', { name: 'Sign in' })).toBeTruthy();
});
it('shows a safe retry for canceled login and strips callback data', async () => {
  auth.isAuthenticated = false; auth.error = new Error('private token detail');
  window.history.replaceState(null, '', '/?error=access_denied&state=private');
  render(<SessionProvider><Account /></SessionProvider>);
  expect(await screen.findByText('Sign-in was canceled or could not be completed. You can try again.')).toBeTruthy();
  expect(screen.getByRole('alert').querySelector('.button.secondary')?.textContent).toBe('Back to start');
  expect(screen.getByRole('heading', { name: 'Unable to sign in' })).toBeTruthy();
  expect(window.location.search).toBe('');
  fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
  expect(auth.loginWithRedirect).toHaveBeenCalledOnce();
});
it('keeps protected calls blocked after remounting a locally signed-out session', () => {
  sessionStorage.setItem('traveltracker.signedOut', 'true');
  render(<SessionProvider><Account /></SessionProvider>);
  expect(screen.queryByRole('button', { name: 'Check API access' })).toBeNull();
  expect(screen.getByRole('button', { name: 'Sign in' })).toBeTruthy();
});
it('blocks calls on logout even when persistence fails', async () => {
  render(<SessionProvider><Account /></SessionProvider>);
  vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new DOMException('Disabled', 'SecurityError'); });
  fireEvent.click(screen.getByRole('button', { name: 'Sign out' }));
  await waitFor(() => expect(screen.queryByRole('button', { name: 'Check API access' })).toBeNull());
  expect(screen.getByRole('button', { name: 'Sign in' })).toBeTruthy();
});
it('keeps navigation usable but fails closed when storage reads are unavailable', () => {
  vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new DOMException('Disabled', 'SecurityError'); });
  render(<SessionProvider><Account /></SessionProvider>);
  expect(screen.queryByRole('button', { name: 'Check API access' })).toBeNull();
  expect(screen.getByRole('button', { name: 'Sign in' })).toBeTruthy();
});
it('offers sign-in again when token renewal reports an expired session', async () => {
  auth.getAccessTokenSilently.mockRejectedValue(new Error('login_required'));
  const fetch = vi.fn(); vi.stubGlobal('fetch', fetch);
  render(<SessionProvider><Account /></SessionProvider>);
  fireEvent.click(screen.getByRole('button', { name: 'Check API access' }));
  expect(await screen.findByRole('button', { name: 'Sign in' })).toBeTruthy();
  expect(screen.queryByRole('button', { name: 'Check API access' })).toBeNull();
  expect(fetch).not.toHaveBeenCalled();
});
it('retains sign-out and a useful permission error for a forbidden API call', async () => {
  auth.getAccessTokenSilently.mockResolvedValue('test-access-token');
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 403 })));
  render(<SessionProvider><Account /></SessionProvider>);
  fireEvent.click(screen.getByRole('button', { name: 'Check API access' }));
  expect(await screen.findByText('You do not have permission for this action.')).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Sign out' })).toBeTruthy();
});
it('blocks further protected calls when the API rejects authentication', async () => {
  auth.getAccessTokenSilently.mockResolvedValue('test-access-token');
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 401 })));
  render(<SessionProvider><Account /></SessionProvider>);
  fireEvent.click(screen.getByRole('button', { name: 'Check API access' }));
  expect(await screen.findByRole('button', { name: 'Sign in' })).toBeTruthy();
  expect(screen.queryByRole('button', { name: 'Check API access' })).toBeNull();
});
it('offers reload instead of an endless session-startup spinner', () => {
  vi.useFakeTimers(); auth.isLoading = true; auth.isAuthenticated = false;
  render(<SessionProvider><Account /></SessionProvider>);
  expect(screen.getByText('Checking your sign-in session…')).toBeTruthy();
  act(() => { vi.advanceTimersByTime(15000); });
  expect(screen.getByRole('heading', { name: 'Sign-in check timed out' })).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Reload' })).toBeTruthy();
  expect(screen.queryByText('Checking your sign-in session…')).toBeNull();
});
