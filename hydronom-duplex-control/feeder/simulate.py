import argparse, math, random, time
from datetime import datetime, timezone
import requests

API = "http://localhost:5000/api/telemetry"

def clamp(v, lo, hi):
    return max(lo, min(hi, v))

def make_sample(vehicle_id: str, vtype: str, lat: float, lon: float, heading: float, speed: float,
                soc: float, leak: bool, temp: float, rudder: float, left_pwm: int, right_pwm: int,
                mode: str, task_id: str | None, wp_index: int | None, depth: float, ballast: int):
    now = datetime.now(timezone.utc).isoformat()
    return {
        "timestamp": now,
        "vehicle": {"id": vehicle_id, "type": vtype},
        "pose": {"lat": lat, "lon": lon, "heading_deg": heading, "speed_mps": speed},
        "depth_m": depth,
        "imu": {"roll_deg": 0.2, "pitch_deg": -1.3, "yaw_deg": heading},
        "thrusters": {"left_pwm": left_pwm, "right_pwm": right_pwm},
        "rudder_deg": rudder,
        "ballast": {"level_pct": ballast},
        "battery": {"voltage": 12.0 + (soc/100.0)*3.0, "soc_pct": soc},
        "leak": leak,
        "temp_c": temp,
        "mission": {"mode": mode, "task_id": task_id, "waypoint_index": wp_index}
    }

def main():
    p = argparse.ArgumentParser()
    p.add_argument("--api", default=API)
    p.add_argument("--vehicle", default="hydronom-boat-01")
    p.add_argument("--type", choices=["boat","sub"], default="boat")
    p.add_argument("--hz", type=float, default=5.0)
    p.add_argument("--leak-after", type=float, default=None, help="seconds to trigger leak")
    p.add_argument("--low-battery", type=float, default=None, help="drop to this SoC% over time")
    p.add_argument("--start-soc", type=float, default=90.0)
    p.add_argument("--start-lat", type=float, default=41.025)
    p.add_argument("--start-lon", type=float, default=28.85)
    args = p.parse_args()

    dt = 1.0/args.hz
    t0 = time.time()

    lat, lon = args.start_lat, args.start_lon
    heading = random.uniform(0, 360)
    speed = 0.8
    soc = args.start_soc
    rudder = 0.0
    left_pwm = 1450
    right_pwm = 1450
    depth = 0.0
    ballast = 0

    leak = False
    temp = 24.0

    while True:
        t = time.time() - t0
        if args.leak-after is not None and t > args.leak-after:
            leak = True
        if args.low-battery is not None and soc > args.low-battery:
            soc = max(args.low-battery, soc - 0.02)  # slow drop

        # simple kinematics
        heading += (right_pwm - left_pwm) * 0.0002
        heading = (heading + 360) % 360
        speed = 0.5 + (left_pwm + right_pwm - 2900) * 0.001  # ~1100..1900
        speed = clamp(speed, 0, 2.5)

        # move ~ rough meters -> degrees (very rough, demo only)
        lat += math.cos(math.radians(heading)) * speed * dt * 1e-5
        lon += math.sin(math.radians(heading)) * speed * dt * 1e-5

        sample = make_sample(args.vehicle, args.type, lat, lon, heading, speed, soc, leak, temp, rudder, left_pwm, right_pwm, "AUTONOMOUS", "task-001", 0, depth, ballast)
        try:
            r = requests.post(args.api, json=sample, timeout=2)
            # print(r.status_code)
        except Exception as e:
            pass
        time.sleep(dt)

if __name__ == "__main__":
    main()
