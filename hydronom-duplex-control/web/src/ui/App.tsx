import { useEffect, useRef, useState } from 'react'
import { Telemetry } from '../types'
import { devToken, sendCommand, wsUrl } from '../api'

const VEHICLE = 'hydronom-boat-01'

export default function App() {
  const [tel, setTel] = useState<Telemetry | null>(null)
  const [token, setToken] = useState<string>('')
  const [mode, setMode] = useState<'MANUAL'|'AUTONOMOUS'|'EMERGENCY'>('AUTONOMOUS')
  const wsRef = useRef<WebSocket | null>(null)

  useEffect(() => {
    devToken().then(t => setToken(t.token))
  }, [])

  useEffect(() => {
    const ws = new WebSocket(wsUrl(VEHICLE))
    wsRef.current = ws
    ws.onmessage = (ev) => {
      const data = JSON.parse(ev.data)
      if (data && data.vehicle) setTel(data)
    }
    ws.onclose = () => {
      // reconnect with exponential backoff (simple)
      setTimeout(() => location.reload(), 1000)
    }
    return () => ws.close()
  }, [])

  async function setManual() {
    await sendCommand(token, {
      timestamp: new Date().toISOString(),
      vehicle_id: VEHICLE,
      command: 'SET_MODE',
      payload: { mode: 'MANUAL' },
    })
    setMode('MANUAL')
  }

  async function setAutonomous() {
    await sendCommand(token, {
      timestamp: new Date().toISOString(),
      vehicle_id: VEHICLE,
      command: 'SET_MODE',
      payload: { mode: 'AUTONOMOUS' },
    })
    setMode('AUTONOMOUS')
  }

  async function setThrusters(left: number, right: number) {
    await sendCommand(token, {
      timestamp: new Date().toISOString(),
      vehicle_id: VEHICLE,
      command: 'SET_THRUSTERS',
      payload: { left_pwm: left, right_pwm: right },
    })
  }

  const critical =
    (tel?.leak === true) ||
    ((tel?.battery.soc_pct ?? 100) < 20) ||
    ((tel?.temp_c ?? 0) > 50)

  return (
    <div className="min-h-screen p-6 space-y-6">
      <header className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Hydronom Operator UI</h1>
        <div className="text-sm opacity-80">Vehicle: <span className="font-mono">{VEHICLE}</span></div>
      </header>

      {critical && (
        <div className="bg-red-700/30 border border-red-600 text-red-200 p-3 rounded-xl">
          <b>CRITICAL:</b> {tel?.leak ? 'Leak detected' : ((tel?.battery.soc_pct ?? 100) < 20) ? 'Low Battery' : 'Overheat'}
        </div>
      )}

      <section className="grid md:grid-cols-3 gap-4">
        <div className="bg-gray-900 p-4 rounded-2xl shadow">
          <h2 className="font-semibold mb-2">Speed / Heading</h2>
          <div className="text-4xl font-bold">{tel?.pose.speed_mps?.toFixed(2) ?? '-'} m/s</div>
          <div className="text-xl">Heading: {tel?.pose.heading_deg?.toFixed(1) ?? '-'}°</div>
        </div>
        <div className="bg-gray-900 p-4 rounded-2xl shadow">
          <h2 className="font-semibold mb-2">Battery</h2>
          <div className="text-4xl font-bold">{tel?.battery.soc_pct?.toFixed(0) ?? '-'}%</div>
          <div className="text-xl">{tel?.battery.voltage?.toFixed(1) ?? '-'} V</div>
        </div>
        <div className="bg-gray-900 p-4 rounded-2xl shadow">
          <h2 className="font-semibold mb-2">Env</h2>
          <div>Temp: {tel?.temp_c?.toFixed(1) ?? '-'} °C</div>
          <div>Leak: {String(tel?.leak ?? false)}</div>
          <div>Mode: {tel?.mission.mode ?? mode}</div>
        </div>
      </section>

      <section className="bg-gray-900 p-4 rounded-2xl shadow space-y-3">
        <h2 className="font-semibold">Manual Controls</h2>
        <div className="flex gap-2">
          <button onClick={setManual} className="px-3 py-2 rounded-xl bg-yellow-600 hover:bg-yellow-500">Set MANUAL</button>
          <button onClick={setAutonomous} className="px-3 py-2 rounded-xl bg-green-600 hover:bg-green-500">Set AUTONOMOUS</button>
        </div>
        <div className="grid sm:grid-cols-2 gap-3">
          <ThrusterSlider label="Left PWM" onChange={(v)=>setThrusters(v, tel?.thrusters.right_pwm ?? 1450)} value={tel?.thrusters.left_pwm ?? 1450} />
          <ThrusterSlider label="Right PWM" onChange={(v)=>setThrusters(tel?.thrusters.left_pwm ?? 1450, v)} value={tel?.thrusters.right_pwm ?? 1450} />
        </div>
      </section>

      <footer className="text-xs opacity-70">MVP demo • JWT dev token is auto-fetched</footer>
    </div>
  )
}

function ThrusterSlider({label, value, onChange}:{label:string, value:number, onChange:(v:number)=>void}){
  return (
    <div>
      <div className="mb-1">{label}: <span className="font-mono">{value}</span></div>
      <input type="range" min={1100} max={1900} step={10} value={value} onChange={e=>onChange(Number(e.target.value))} className="w-full" />
    </div>
  )
}
