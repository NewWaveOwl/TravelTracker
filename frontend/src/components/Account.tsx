import { useEffect, useRef, useState } from 'react';
import { useSession } from '../auth/SessionProvider';
import { ApiError } from '../api/client';
import Feedback, { type FeedbackKind } from './Feedback';

export default function Account() {
  const session = useSession();
  const [result, setResult] = useState<{ kind: FeedbackKind; text: string } | null>(null);
  const [startupTimedOut, setStartupTimedOut] = useState(false);
  const request = useRef<AbortController | null>(null);
  useEffect(() => () => request.current?.abort(), []);
  useEffect(() => { if (!session.authenticated) { request.current?.abort(); setResult(null); } }, [session.authenticated]);
  useEffect(() => {
    setStartupTimedOut(false);
    if (!session.loading) return;
    const timer = window.setTimeout(() => setStartupTimedOut(true), 15000);
    return () => window.clearTimeout(timer);
  }, [session.loading]);
  if (!session.configured) return <Feedback kind="unavailable">Sign-in is temporarily unavailable. Please try again later.</Feedback>;
  if (session.loading && startupTimedOut) return <Feedback kind="network" title="Sign-in check timed out" eyebrow="CONNECTION ERROR" retry={() => window.location.reload()} retryLabel="Reload" backToStart>We could not check your session. Reload to try again, or return to the start.</Feedback>;
  if (session.loading) return <Feedback kind="loading">Checking your sign-in session…</Feedback>;
  if (session.message && !session.authenticated && session.message !== 'You are signed out.') return <Feedback kind="unexpected" title="Unable to sign in" eyebrow="SIGN-IN ERROR" retry={() => void session.signIn()} retryLabel="Sign in" backToStart>{session.message}</Feedback>;
  const checkAccess = async () => {
    request.current?.abort();
    const controller = new AbortController(); request.current = controller;
    setResult({ kind: 'loading', text: 'Checking access to Transport…' });
    try {
      await session.get('Transport', '/api/v1/transport-modes/00000000-0000-0000-0000-000000000101', controller.signal);
      if (!controller.signal.aborted) setResult({ kind: 'success', text: 'Your protected request completed successfully.' });
    } catch (error) {
      if (!controller.signal.aborted) setResult({ kind: error instanceof ApiError ? error.kind : 'unexpected', text: error instanceof ApiError ? error.message : 'The request could not be completed.' });
    }
  };
  return <section className="form-panel"><h2>{session.authenticated ? 'You are signed in' : 'Sign in'}</h2>
    {session.message && <p role="status">{session.message}</p>}
    <div className="actions">{session.authenticated ? <><button onClick={checkAccess} disabled={result?.kind === 'loading'}>Check API access</button><button className="secondary" onClick={() => void session.signOut()}>Sign out</button></> : <button onClick={() => void session.signIn()}>Sign in</button>}</div>
    {result && <Feedback kind={result.kind}>{result.text}</Feedback>}
  </section>;
}
