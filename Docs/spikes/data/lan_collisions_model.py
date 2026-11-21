"""Model of two drone balls meeting over a LAN. Not Unity physics: two discs, 1 kHz steps, to compare
A) each PC simulates its own ball and bounces off the delayed copy of the other one, with
B) the host simulates both balls and the PCs only send sticks."""
import numpy as np
rng = np.random.default_rng(7)
R, E, DT = 0.20, 0.6, 0.001          # 40 cm ball, restitution, step
def bounce(p1, v1, p2, v2):
    """Two equal masses: velocity change of ball 1."""
    n = p1 - p2; d = np.linalg.norm(n)
    if d >= 2 * R or d == 0: return None
    n /= d; rel = np.dot(v1 - v2, n)
    if rel >= 0: return None
    return -(1 + E) * rel / 2 * n
def truth(pa, va, pb, vb, T):
    for _ in range(int(T / DT)):
        j = bounce(pa, va, pb, vb)
        if j is not None: va = va + j; vb = vb - j
        pa = pa + va * DT; pb = pb + vb * DT
    return pa, pb
def own_ball(pa, va, pb, vb, T, lat, rate, extrapolate, latch=0.0):
    """Each side keeps its own ball; the other one is known `lat` late, refreshed `rate` times a second."""
    hist = []; hit = [False, False]; hit_t = [None, None]
    send = max(1, int(round(1 / rate / DT))); lag = int(round(lat / DT))
    for k in range(int(T / DT)):
        hist.append((pa.copy(), va.copy(), pb.copy(), vb.copy()))
        ks = max(0, ((k - lag) // send) * send)           # the newest snapshot that has arrived
        age = (k - ks) * DT
        qa, wa, qb, wb = hist[ks]
        ra = qa + wa * age if extrapolate else qa; rb = qb + wb * age if extrapolate else qb
        ja = bounce(pa, va, rb, wb); jb = bounce(pb, vb, ra, wa)
        if ja is not None and (hit_t[0] is None or k * DT - hit_t[0] > latch): va = va + ja; hit[0] = True; hit_t[0] = k * DT
        if jb is not None and (hit_t[1] is None or k * DT - hit_t[1] > latch): vb = vb + jb; hit[1] = True; hit_t[1] = k * DT
        pa = pa + va * DT; pb = pb + vb * DT
    return pa, pb, hit, hit_t
print('latency  rate  extrap | one-sided hits | end position error vs ideal, cm (median / 95%) | on-screen overlap or gap at contact, cm')
for lat_ms in (1, 5, 20, 50):
    for rate in (60, 120):
        for ex in (True,):
            errs = []; one = 0; n = 0; gaps = []
            for _ in range(400):
                speed = rng.uniform(2, 8); off = rng.uniform(-0.3, 0.3)
                pa = np.array([-1.5, off]); va = np.array([speed, 0.0])
                pb = np.array([1.5, 0.0]); vb = np.array([-rng.uniform(0, 6), rng.uniform(-1, 1)])
                tc = 3.0 / (va[0] - vb[0]); T = tc + 0.2
                ia, ib = truth(pa.copy(), va.copy(), pb.copy(), vb.copy(), T)
                a, b, hit, ht = own_ball(pa.copy(), va.copy(), pb.copy(), vb.copy(), T, lat_ms / 1000, rate, ex, latch=0.15)
                th = np.linalg.norm(truth(pa.copy(), va.copy(), pb.copy(), vb.copy(), tc)[0] - truth(pa.copy(), va.copy(), pb.copy(), vb.copy(), tc)[1]) < 2 * R + 0.02
                if hit[0] or hit[1] or th:
                    n += 1; one += hit[0] != hit[1]
                    errs.append(max(np.linalg.norm(a - ia), np.linalg.norm(b - ib)) * 100)
                    stale = (lat_ms / 1000 + 0.5 / rate) * (va[0] - vb[0]) * 100
                    gaps.append(0.0 if ex else stale)
            print(f'{lat_ms:4d} ms {rate:4d} Hz {"yes" if ex else "no ":>5} | {100 * one / n:5.1f}% | {np.median(errs):5.1f} / {np.percentile(errs, 95):5.1f} | {np.median(gaps):4.1f}')
print()
print('host simulates both: added stick delay = to the host + back + half a host step + interpolation buffer')
for lat_ms in (1, 5, 20, 50):
    for rate in (60, 120):
        print(f'{lat_ms:4d} ms {rate:4d} Hz : {2 * lat_ms + 500 / rate + 2 * 1000 / rate:5.1f} ms added to every stick movement; the two PCs always agree')
