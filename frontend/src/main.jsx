import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import App from './App'
import { ConfirmHost } from './components/ConfirmDialog'
import './styles/index.css'

createRoot(document.getElementById('root')).render(
  <StrictMode>
    <App />
    <ConfirmHost />
  </StrictMode>,
)
