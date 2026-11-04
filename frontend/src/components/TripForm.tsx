import { useState } from 'react';
import Feedback from './Feedback';

export default function TripForm() {
  const [submitted, setSubmitted] = useState(false);
  return <form className="form-panel" onSubmit={event => { event.preventDefault(); setSubmitted(true); }}>
    <label htmlFor="name">Name *<input id="name" name="name" required placeholder="Enter a name" /></label>
    <label htmlFor="description">Description *<input id="description" name="description" required placeholder="Enter a description" /></label>
    <small>Trip saving will be available when the Trips service is connected.</small>
    {submitted && <Feedback kind="unavailable">Your details have not been saved. Trip creation is coming in a separate feature.</Feedback>}
    <div className="actions"><button type="submit">Save item</button><a className="button secondary" href="#/">Cancel</a></div>
  </form>;
}
