import { useEffect, useRef, useState } from 'react';
import { useSession } from '../auth/SessionProvider';
import { ApiError } from '../api/client';
import Feedback, { type FeedbackKind } from './Feedback';

export default function Account() {
  const session = useSession();
  const [result, setResult] = useState<{ kind: FeedbackKind; text: string } | null>(null);
  const request = useRef<AbortController | null>(null);
  useEffect(() => () => request.current?.abort(), []);
  useEffect(() => { if (!session.authenticated) { request.current?.abort(); setResult(null); } }, [session.authenticated]);
  if (!session.configured) return <Feedback kind="unavailable">Sign-in setup is pending. Configure the Auth0 application using the frontend setup guide.</Feedback>;
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
  return <section className="form-panel"><h2>{session.authenticated ? 'You are signed in' : 'Sign in to TravelTracker'}</h2>
    <p>Use your Auth0 account to access protected travel services.</p>
    {session.message && <p role="status">{session.message}</p>}
    <div className="actions">{session.authenticated ? <><button onClick={checkAccess} disabled={result?.kind === 'loading'}>Check API access</button><button className="secondary" onClick={() => void session.signOut()}>Sign out</button></> : <button onClick={() => void session.signIn()}>Sign in</button>}</div>
    {result && <Feedback kind={result.kind}>{result.text}</Feedback>}
  </section>;
}
