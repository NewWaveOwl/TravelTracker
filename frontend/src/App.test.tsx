// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import App from './App';
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; });
it('keeps navigation usable after an unavailable service', async () => {
  window.location.hash = '/services';
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 503 })));
  render(<App />);
  expect(await screen.findByRole('heading', { name: 'Service unavailable' })).toBeTruthy();
  fireEvent.click(screen.getByRole('link', { name: 'History' }));
  window.location.hash = '/history';
  fireEvent(window, new HashChangeEvent('hashchange'));
  expect(await screen.findByRole('heading', { name: 'History' })).toBeTruthy();
});
it('opens the labelled menu and links to service status', () => {
  render(<App />);
  fireEvent.click(screen.getByRole('button', { name: 'Menu' }));
  expect(screen.getByRole('link', { name: 'Service status' }).getAttribute('href')).toBe('#/services');
});
