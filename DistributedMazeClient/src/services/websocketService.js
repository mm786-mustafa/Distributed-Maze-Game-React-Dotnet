// services/WebSocketService.js
import config from "../config";

class WebSocketService {
  constructor() {
    this.socket = null;
    this.sessionId = null;
    this.playerId = null;
    this.reconnectInterval = 3000; // 3 seconds
    this.shouldReconnect = true;
    this.listeners = new Map(); // type -> Set(callback)
  }

  get urlBase() {
    const env = config.backendUrl;
    if (env && env.startsWith("ws")) return env; // full ws(s)://host[:port]/ws
    // Fallback: derive from page location
    const loc = window.location;
    const protocol = loc.protocol === "https:" ? "wss:" : "ws:";
    const host = loc.host; // includes port if any
    return `${protocol}//${host}/ws`;
  }

  connect(sessionId) {
    if (this.socket && this.socket.readyState === WebSocket.OPEN && this.sessionId === sessionId) {
      return; // already connected to this session
    }

    this.sessionId = sessionId;
    this.shouldReconnect = true;

    const url = `${this.urlBase}?sessionId=${encodeURIComponent(sessionId)}`;
    this.socket = new WebSocket(url);

    this.socket.onopen = () => {
      // Connected; server will assign player via ASSIGNED
      // Optionally send a join message if protocol required
      // this.send("JOIN", { sessionId: this.sessionId });
    };

    this.socket.onmessage = (event) => {
      try {
        const msg = JSON.parse(event.data);
        const { type, payload } = msg;

        // Special handling: assignment
        if (type === "ASSIGNED" && payload && payload.playerId != null) {
          this.playerId = payload.playerId;
        }

        this.#emit(type, payload ?? msg);
      } catch (err) {
        console.error("Failed to parse WebSocket message", err);
      }
    };

    this.socket.onclose = () => {
      if (this.shouldReconnect) {
        setTimeout(() => this.connect(this.sessionId), this.reconnectInterval);
      }
    };

    this.socket.onerror = () => {
      try { this.socket.close(); } catch {}
    };
  }

  disconnect() {
    this.shouldReconnect = false;
    if (this.socket) {
      try { this.socket.close(); } catch {}
      this.socket = null;
    }
  }

  send(type, payload = {}) {
    if (!this.socket || this.socket.readyState !== WebSocket.OPEN) return;
    const msg = { type, ...payload };
    if (!msg.sessionId && this.sessionId) msg.sessionId = this.sessionId;
    if (!msg.playerId && this.playerId != null) msg.playerId = this.playerId;
    this.socket.send(JSON.stringify(msg));
  }

  subscribe(type, callback) {
    if (!this.listeners.has(type)) this.listeners.set(type, new Set());
    this.listeners.get(type).add(callback);
  }

  unsubscribe(type, callback) {
    if (!this.listeners.has(type)) return;
    if (callback) {
      this.listeners.get(type).delete(callback);
    } else {
      this.listeners.delete(type);
    }
  }

  #emit(type, payload) {
    const set = this.listeners.get(type);
    if (!set) return;
    for (const cb of set) {
      try { cb(payload); } catch {}
    }
  }
}

const websocketService = new WebSocketService();
export default websocketService;
