import { Auth0Provider, useAuth0 } from '@auth0/auth0-react';
import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import { authConfig } from './config';
import { loginFailure } from './loginFailure';
import { protectedGet } from './protectedRequest';
import { ApiError, type serviceUrls } from '../api/client';

type Session = {
  configured: boolean; loading: boolean; authenticated: boolean; message: string;
  signIn: () => Promise<void>; signOut: () => Promise<void>;
  get: <T>(service: keyof typeof serviceUrls, path: string, signal?: AbortSignal) => Promise<T>;
};
const SessionContext = createContext<Session>({ configured: false, loading: false, authenticated: false, message: '', signIn: async () => {}, signOut: async () => {}, get: async () => { throw new ApiError('permission', 'Sign-in is not configured.', 401); } });
export const useSession = () => useContext(SessionContext);

function SessionBridge({ children }: { children: ReactNode }) {
  const auth = useAuth0();
  const [blocked, setBlocked] = useState(() => {
    try { return sessionStorage.getItem('traveltracker.signedOut') === 'true'; }
    catch { return true; }
  });
  const [message, setMessage] = useState('');
  const active = useRef(false);
  const epoch = useRef(0);
  const requests = useRef(new AbortController());
  active.current = auth.isAuthenticated && !blocked;
  const block = (text: string) => {
    active.current = false; epoch.current++; requests.current.abort();
    requests.current = new AbortController(); setBlocked(true); setMessage(text);
    try { sessionStorage.setItem('traveltracker.signedOut', 'true'); }
    catch { /* The in-memory block must still take effect when storage is unavailable. */ }
  };
  useEffect(() => {
    requests.current = new AbortController();
    return () => requests.current.abort();
  }, []);
  useEffect(() => {
    if (!auth.error) return;
    // Never render provider error descriptions, callback parameters or token data.
    setMessage(loginFailure(auth.error));
    window.history.replaceState(null, '', `${window.location.pathname}#/account`);
    window.dispatchEvent(new HashChangeEvent('hashchange'));
  }, [auth.error]);
  const session: Session = {
    configured: true, loading: auth.isLoading, authenticated: auth.isAuthenticated && !blocked, message,
    signIn: async () => {
      setMessage('');
      try { sessionStorage.removeItem('traveltracker.signedOut'); }
      catch { setMessage('Browser session storage is unavailable. Enable it for this site to sign in.'); return; }
      try { await auth.loginWithRedirect({ appState: { returnTo: '/account' } }); }
      catch { setMessage('Unable to start sign-in. Please try again.'); }
    },
    signOut: async () => {
      block('You are signed out.');
      try { await auth.logout({ logoutParams: { returnTo: window.location.origin } }); }
      catch { setMessage('Signed out locally. Provider logout could not be completed. Use Sign in when you want to reconnect.'); }
    },
    get: async <T,>(service: keyof typeof serviceUrls, path: string, signal?: AbortSignal) => {
      const startEpoch = epoch.current;
      return protectedGet<T>(service, path, {
        getToken: async () => {
          const token = await auth.getAccessTokenSilently();
          if (!token) throw new Error('Access token unavailable.');
          return token;
        },
        isActive: () => active.current && startEpoch === epoch.current,
        expire: () => block('Your session has expired or was rejected. Sign in again to continue.'),
      }, signal ? AbortSignal.any([signal, requests.current.signal]) : requests.current.signal);
    },
  };
  return <SessionContext.Provider value={session}>{children}</SessionContext.Provider>;
}

export default function SessionProvider({ children }: { children: ReactNode }) {
  if (!authConfig) return <>{children}</>;
  return <Auth0Provider domain={authConfig.domain} clientId={authConfig.clientId}
    cacheLocation="memory" useRefreshTokens={false}
    authorizeTimeoutInSeconds={10} httpTimeoutInSeconds={10}
    authorizationParams={{ redirect_uri: window.location.origin, audience: authConfig.audience, scope: authConfig.scope }}
    onRedirectCallback={() => {
      window.history.replaceState(null, '', `${window.location.pathname}#/account`);
      window.dispatchEvent(new HashChangeEvent('hashchange'));
    }}>
    <SessionBridge>{children}</SessionBridge>
  </Auth0Provider>;
}
