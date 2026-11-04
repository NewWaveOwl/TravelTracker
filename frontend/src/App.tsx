import { useEffect, useRef, useState } from 'react';
import Feedback from './components/Feedback';
import HealthCheck from './components/HealthCheck';
import TripForm from './components/TripForm';

const titles: Record<string, string> = { '/': 'Welcome to TravelTracker', '/new-trip': 'New Trip', '/history': 'History', '/settings': 'Settings', '/account': 'Your account', '/services': 'Service status' };

export default function App() {
  const [path, setPath] = useState(location.hash.slice(1) || '/');
  const [menu, setMenu] = useState(false);
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    const navigate = () => { setPath(location.hash.slice(1) || '/'); setMenu(false); };
    window.addEventListener('hashchange', navigate);
    return () => window.removeEventListener('hashchange', navigate);
  }, []);
  useEffect(() => { document.title = `${titles[path] || 'Page not found'} | TravelTracker`; heading.current?.focus(); }, [path]);
  return <><a className="skip" href="#main" onClick={event => { event.preventDefault(); heading.current?.focus(); }}>Skip to content</a><header>
    <a className="brand" href="#/">TravelTracker<img src="/assets/logo.svg" alt="" /></a>
    <nav aria-label="Main navigation"><a aria-current={path === '/new-trip' ? 'page' : undefined} href="#/new-trip">New Trip</a><a aria-current={path === '/history' ? 'page' : undefined} href="#/history">History</a></nav>
    <a className="icon" href="#/account" aria-label="Your account"><img src="/assets/account.svg" alt="" /></a>
    <button className="icon" aria-label="Menu" aria-expanded={menu} aria-controls="menu" onClick={() => setMenu(!menu)}><img src="/assets/menu.svg" alt="" /></button>
    {menu && <nav id="menu" className="menu" aria-label="More navigation"><a href="#/settings">Settings</a><a href="#/services">Service status</a></nav>}
  </header><main id="main"><h1 ref={heading} tabIndex={-1}>{titles[path] || 'Page not found'}</h1>
    {path === '/' ? <><p>Plan your next journey, all in one place.</p><section className="intro"><h2>Make room for your next adventure.</h2><p>Choose your destination and start building your itinerary.</p><a className="button large" href="#/new-trip">New Trip</a></section><section className="intro returning"><h2>Pick up where you left off</h2><p>Use History in the header to return to your previous trips.</p></section></>
    : path === '/new-trip' ? <><p>Add the details below. Fields marked * are required.</p><TripForm /></>
    : path === '/history' ? <><p>View and manage your saved trips.</p><Feedback kind="empty">Trip history will appear here when the Trips service is connected.</Feedback></>
    : path === '/services' ? <HealthCheck />
    : path === '/account' ? <Feedback kind="permission">Sign-in will be connected in the identity integration feature.</Feedback>
    : path === '/settings' ? <Feedback kind="empty">Your preferences will be available when the Settings service is connected.</Feedback>
    : <Feedback kind="empty"><a href="#/">Back to start</a></Feedback>}
  </main></>;
}
