import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import { TripPortal } from './trips/TripPortal.tsx'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <TripPortal />
  </StrictMode>,
)
