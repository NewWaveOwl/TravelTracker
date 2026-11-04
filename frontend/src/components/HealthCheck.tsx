import { useEffect, useState } from 'react';
import { ApiError, getJson, serviceUrls } from '../api/client';
import Feedback, { type FeedbackKind } from './Feedback';

export default function HealthCheck() {
  const [service, setService] = useState<keyof typeof serviceUrls>('Attractions');
  const [attempt, setAttempt] = useState(0);
  const [kind, setKind] = useState<FeedbackKind>('loading');
  useEffect(() => {
    const controller = new AbortController();
    setKind('loading');
    getJson<unknown>(`${serviceUrls[service].replace(/\/$/, '')}/health`, controller.signal)
      .then(() => { if (!controller.signal.aborted) setKind('success'); })
      .catch(error => { if (!controller.signal.aborted) setKind(error instanceof ApiError ? error.kind : 'unexpected'); });
    return () => controller.abort();
  }, [service, attempt]);
  return <><label htmlFor="service">Service</label><select id="service" value={service} onChange={event => setService(event.target.value as keyof typeof serviceUrls)}>{Object.keys(serviceUrls).map(name => <option key={name}>{name}</option>)}</select>
    <Feedback kind={kind} retry={kind === 'loading' ? undefined : () => setAttempt(value => value + 1)}>{kind === 'success' ? `${service} responded to its public health check.` : kind === 'loading' ? `Checking ${service}…` : 'The service could not be reached. Try again shortly; you can keep using navigation.'}</Feedback></>;
}
