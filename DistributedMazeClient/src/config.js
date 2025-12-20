const config = {
  // Prefer Vite env, fallback to CRA-style for compatibility
  backendUrl:
    (typeof import.meta !== 'undefined' ? import.meta.env?.VITE_BACKEND_URL : undefined) ||
    (typeof process !== 'undefined' ? process.env?.REACT_APP_BACKEND_URL : undefined),
};

// If env not provided, websocketService.urlBase will derive from window.location

export default config;
