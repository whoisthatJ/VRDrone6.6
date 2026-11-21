"""The MAVLink sequences of the QGroundControl checklist, driven with pymavlink as the ground station."""
import sys, time
from pymavlink import mavutil
m = mavutil.mavlink_connection('udpin:127.0.0.1:14550', dialect='ardupilotmega')
ok = []
def check(name, cond, note=''):
    ok.append(bool(cond)); print(('PASS ' if cond else 'FAIL ') + name + (' : ' + str(note) if note != '' else ''))
hb = m.wait_heartbeat(timeout=10)
check('heartbeat within 5 s', hb is not None, f'type {hb.type} autopilot {hb.autopilot} mode {m.flightmode}' if hb else '')
check('presents as ArduPilot quadrotor', hb and hb.autopilot == 3 and hb.type == 2)
def recv(t, timeout=5, cond=None):
    end = time.time() + timeout
    while time.time() < end:
        x = m.recv_match(type=t, blocking=True, timeout=0.5)
        if x is not None and (cond is None or cond(x)): return x
    return None
def cmd(c, *p, timeout=5):
    p = list(p) + [0] * (7 - len(p))
    m.mav.command_long_send(m.target_system, m.target_component, c, 0, *p)
    a = recv('COMMAND_ACK', timeout, lambda x: x.command == c)
    return a.result if a else None
# parameters
m.mav.param_request_list_send(m.target_system, m.target_component)
params = {}; end = time.time() + 10; count = None
while time.time() < end and (count is None or len(params) < count):
    p = m.recv_match(type='PARAM_VALUE', blocking=True, timeout=1)
    if p: params[p.param_id] = p.param_value; count = p.param_count
check('parameter list complete', count and len(params) == count, f'{len(params)} of {count}')
m.mav.param_set_send(m.target_system, m.target_component, b'RTL_ALT', 2000, 9)
p = recv('PARAM_VALUE', 5, lambda x: x.param_id == 'RTL_ALT')
check('parameter change echoed', p and abs(p.param_value - 2000) < 1e-3, p.param_value if p else None)
check('autopilot version on request', cmd(512, 148) == 0 and recv('AUTOPILOT_VERSION', 3) is not None)
# telemetry
for t in ['ATTITUDE', 'GLOBAL_POSITION_INT', 'GPS_RAW_INT', 'SYS_STATUS', 'BATTERY_STATUS', 'HOME_POSITION', 'VFR_HUD']:
    check('telemetry ' + t, recv(t, 5) is not None)
# mode list, arm, take off
check('take-off refused when disarmed', cmd(22, 0, 0, 0, 0, 0, 0, 5) == 4)
m.set_mode('GUIDED'); time.sleep(1.2); m.wait_heartbeat(timeout=3)
check('mode switched to GUIDED', m.flightmode == 'GUIDED', m.flightmode)
check('arm', cmd(400, 1) == 0); m.wait_heartbeat(timeout=3)
check('heartbeat shows armed', m.motors_armed())
check('take off accepted', cmd(22, 0, 0, 0, 0, 0, 0, 5) == 0)
g = recv('GLOBAL_POSITION_INT', 20, lambda x: x.relative_alt > 4800)
check('reached 5 m', g is not None, g.relative_alt / 1000 if g else None)
# go to a point 20 m north
lat0, lon0 = g.lat, g.lon
m.mav.set_position_target_global_int_send(0, m.target_system, m.target_component, 6, 0x0FF8, lat0 + 1800, lon0, 5, 0, 0, 0, 0, 0, 0, 0, 0)
g = recv('GLOBAL_POSITION_INT', 20, lambda x: abs(x.lat - (lat0 + 1800)) < 30)
check('go to location', g is not None, f'{(g.lat - lat0) * 0.0111:.1f} m north' if g else None)
check('unknown command answered unsupported', cmd(31010) == 3)
# mission: home + take-off + 3 waypoints + land, upload, download, compare, fly
items = [(16, 0, 0, 0), (22, 0, 0, 6)] + [(16, lat0 + dy, lon0 + dx, 6) for dy, dx in ((900, 0), (900, 1400), (0, 1400))] + [(20, 0, 0, 0)]
m.mav.mission_count_send(m.target_system, m.target_component, len(items), 0)
sent = 0
while True:
    r = recv(['MISSION_REQUEST_INT', 'MISSION_REQUEST', 'MISSION_ACK'], 5)
    if r is None or r.get_type() == 'MISSION_ACK': break
    c, x, y, z = items[r.seq]
    m.mav.mission_item_int_send(m.target_system, m.target_component, r.seq, 3, c, 0, 1, 0, 0, 0, 0, x, y, z, 0); sent += 1
check('mission upload accepted', r is not None and r.get_type() == 'MISSION_ACK' and r.type == 0 and sent == len(items), f'{sent} items')
m.mav.mission_request_list_send(m.target_system, m.target_component, 0)
c = recv('MISSION_COUNT', 5)
back = []
for i in range(c.count if c else 0):
    m.mav.mission_request_int_send(m.target_system, m.target_component, i, 0)
    it = recv('MISSION_ITEM_INT', 5, lambda x: x.seq == i)
    if it: back.append((it.command, it.x, it.y, int(it.z)))
m.mav.mission_ack_send(m.target_system, m.target_component, 0, 0)
check('mission downloaded unchanged', back == [(a, b, c2, int(d)) for a, b, c2, d in items], f'{len(back)} items')
m.set_mode('AUTO'); time.sleep(1.2); m.wait_heartbeat(timeout=3)
check('mode AUTO', m.flightmode == 'AUTO', m.flightmode)
seen = set(); end = time.time() + 90; disarmed = False
while time.time() < end:
    x = m.recv_match(type=['MISSION_CURRENT', 'HEARTBEAT'], blocking=True, timeout=1)
    if x is None: continue
    if x.get_type() == 'MISSION_CURRENT': seen.add(x.seq)
    elif not (x.base_mode & 128): disarmed = True; break
check('mission flown to the end and disarmed', disarmed and {2, 3, 4} <= seen, sorted(seen))
g = recv('GLOBAL_POSITION_INT', 5)
check('landed at home', g and g.relative_alt < 200 and abs(g.lat - (lat0 - 0)) < 2500, g.relative_alt if g else None)
print(f'{sum(ok)} of {len(ok)} passed')
