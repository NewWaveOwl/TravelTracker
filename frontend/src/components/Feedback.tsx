import type { ReactNode } from 'react';

export type FeedbackKind = 'loading' | 'empty' | 'validation' | 'permission' | 'network' | 'unavailable' | 'unexpected' | 'success';
const headings: Record<FeedbackKind, string> = {
  loading: 'Loading…', empty: 'Nothing here yet', validation: 'Check your details', permission: 'Permission denied', network: 'Unable to connect', unavailable: 'Service unavailable', unexpected: 'Something went wrong', success: 'Service is available',
};

export default function Feedback({ kind, children, retry, title, eyebrow, backToStart = false, retryLabel = 'Retry' }: { kind: FeedbackKind; children: ReactNode; retry?: () => void; title?: string; eyebrow?: string; backToStart?: boolean; retryLabel?: string }) {
  const error = ['validation', 'permission', 'network', 'unavailable', 'unexpected'].includes(kind);
  return <section className={`feedback ${error ? 'error' : ''}`} role={error ? 'alert' : 'status'} aria-busy={kind === 'loading'}>
    {eyebrow && <small>{eyebrow}</small>}
    <h2>{title || headings[kind]}</h2><p>{children}</p>{retry && <button onClick={retry}>{retryLabel}</button>}
    {backToStart && <a className="button secondary" href="#/">Back to start</a>}
  </section>;
}
