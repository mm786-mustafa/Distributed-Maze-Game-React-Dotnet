// =============================================================================
// config.js
// =============================================================================
// CLIENT CONFIGURATION - Environment-based settings
// 
// Supports both Vite (import.meta.env) and Create React App (process.env)
// If no environment variables are set, derives URLs from window.location
// =============================================================================

const config = {
  // WebSocket backend URL (for game communication)
  // Prefer Vite env, fallback to CRA-style for compatibility
  backendUrl:
    (typeof import.meta !== 'undefined' ? import.meta.env?.VITE_BACKEND_URL : undefined) ||
    (typeof process !== 'undefined' ? process.env?.REACT_APP_BACKEND_URL : undefined),
  
  // REST API URL (for leaderboard and other HTTP endpoints)
  // Defaults to empty string (same origin) if not specified
  apiUrl:
    (typeof import.meta !== 'undefined' ? import.meta.env?.VITE_API_URL : undefined) ||
    (typeof process !== 'undefined' ? process.env?.REACT_APP_API_URL : undefined) ||
    '',
};

// If env not provided, websocketService.urlBase will derive from window.location
// apiUrl defaults to same origin (empty string)

export default config;
