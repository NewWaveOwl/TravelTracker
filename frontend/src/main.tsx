import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import '@fontsource/aldrich';
import '@fontsource/atkinson-hyperlegible';
import '@fontsource/konkhmer-sleokchher';
import App from './App';
import './styles.css';

createRoot(document.getElementById('root')!).render(<StrictMode><App /></StrictMode>);
