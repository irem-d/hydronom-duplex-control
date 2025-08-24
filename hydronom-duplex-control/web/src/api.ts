const API = import.meta.env.VITE_API_BASE ?? 'http://localhost:5000';

export async function getState(vehicleId: string) {
  const r = await fetch(`${API}/api/state/${vehicleId}`);
  if (!r.ok) throw new Error('failed to get state');
  return r.json();
}

export async function sendCommand(token: string, body: any) {
  const r = await fetch(`${API}/api/commands`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${token}` },
    body: JSON.stringify(body),
  });
  if (!r.ok) throw new Error('failed to send command');
  return r.json();
}

export async function devToken(user = 'operator') {
  const r = await fetch(`${API}/auth/dev-token?user=${encodeURIComponent(user)}`);
  return r.json();
}

export function wsUrl(vehicleId: string) {
  return `${API.replace('http', 'ws')}/ws/telemetry?vehicleId=${encodeURIComponent(vehicleId)}`;
}
