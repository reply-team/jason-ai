import { createRoot } from 'react-dom/client';
import { App } from './app';
import './styles/app.css';

createRoot(document.getElementById('app')!).render(<App />);
