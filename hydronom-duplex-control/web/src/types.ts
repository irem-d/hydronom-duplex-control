export type MissionMode = 'MANUAL' | 'AUTONOMOUS' | 'EMERGENCY';

export interface Telemetry {
  timestamp: string;
  vehicle: { id: string; type: 'boat' | 'sub' };
  pose: { lat: number; lon: number; heading_deg: number; speed_mps: number };
  depth_m: number;
  imu: { roll_deg: number; pitch_deg: number; yaw_deg: number };
  thrusters: { left_pwm: number; right_pwm: number };
  rudder_deg: number;
  ballast: { level_pct: number };
  battery: { voltage: number; soc_pct: number };
  leak: boolean;
  temp_c: number;
  mission: { mode: MissionMode; task_id?: string; waypoint_index?: number };
}
