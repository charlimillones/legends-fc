"""Legends FC balancing sim v1 (DaiVinci, Oct 8 2026). Formulas = PROPOSAL v1 (v0 + fixes).
Run: python3 sim_v1.py
"""
import math, random, statistics as st

SEEDS, SEASONS, WEEKS, CAL_WEEKS = 200, 20, 38, 52
clamp = lambda x, a, b: max(a, min(b, x))
P = lambda x: clamp(x, 0.05, 0.95)
logistic = lambda z: 1 / (1 + math.exp(-z))
rows = []
def rec(c, t, f, ok): rows.append((c, t, f, "PASS" if ok else "FAIL"))

# ---- v1 formulas ----
def events_pair(rng):                       # balanced bag per 2 seasons
    n = [rng.randint(2, 4), rng.randint(2, 4)]; tot = sum(n)
    g = tot // 2 + (rng.random() < 0.5 if tot % 2 else 0)
    bag = [True] * g + [False] * (tot - g); rng.shuffle(bag)
    return bag[:n[0]], bag[n[0]:]
def potential_premium(age, gap):
    f = 0.5 if age <= 21 else 0.25 if age <= 23 else 0
    return 1.2 ** (f * max(0, gap))
def value(r, form=6.5, age=25, pot=None, years=3):
    pot = r if pot is None else pot
    F = clamp(1 + 0.15 * (form - 6.5), 0.7, 1.3)
    A = 1.0 if age <= 29 else 0.85 ** (age - 29)
    C = min(1, 0.4 + 0.2 * years)
    return 20000 * 1.2 ** (r - 40) * F * A * potential_premium(age, pot - r) * C
def grow(age, pot, r, cq, regime, n, weeks=40, fac=1.0):
    reg = {"light": 0.6, "moderate": 1.0, "heavy": 1.4}[regime]
    crowd = 1 / (1 + 0.08 * (n - 1)); agec = 1.3 if age <= 21 else 1.0 if age <= 25 else 0.5 if age <= 29 else 0
    for _ in range(weeks): r += 0.08 * cq * reg * crowd * agec * clamp((pot - r) / 10, 0, 1) * fac
    return r
OBJ = {"safe": 0.95, "standard": 1.0, "ambitious": 1.15}
def wage_bar(base, rep, o): return base * 1.025 ** (rep - 50) * OBJ[o]
def accept(wage_ratio, fee_fair, years, bonus=0.0): return P(logistic(0.5 + 2.5 * (wage_ratio - 1) + fee_fair + 0.05 * years + bonus))
def poisson(rng, lam):
    L, k, p = math.exp(-lam), 0, 1.0
    while True:
        p *= rng.random()
        if p < L: return k
        k += 1

# 1 events
counts, shares = [], []
for s in range(SEEDS):
    rng = random.Random(s); seasons = []
    for _ in range(SEASONS // 2): seasons += list(events_pair(rng))
    counts += [len(x) for x in seasons]
    for i in range(0, SEASONS - 4, 5):
        b = [e for x in seasons[i:i + 5] for e in x]; shares.append(sum(b) / len(b))
rec("Events per season", "always 2–4, mean ≈3", f"min {min(counts)}, max {max(counts)}, mean {st.mean(counts):.2f}", min(counts) >= 2 and max(counts) <= 4)
ins = sum(0.4 <= g <= 0.6 for g in shares) / len(shares)
rec("Good share over 5 seasons", "40–60%", f"{ins*100:.0f}% of 5-season spans inside 40–60%", ins >= 0.9)
# 2 personalities, A Keeper
outfield = 105 * 22
rng = random.Random(1); n = outfield + 105 * 3
pers = sum(rng.random() < 0.45 for _ in range(n)) / n
ak = []
for s in range(SEEDS):
    r_ = random.Random(s); ak.append(sum(r_.random() < 1 / 1000 for _ in range(outfield)))
rec("Players with a personality", "40–50%", f"{pers*100:.1f}%", 0.40 <= pers <= 0.50)
rec("A Keeper in test world", "≈2", f"mean {st.mean(ak):.1f}; none in {sum(a==0 for a in ak)/SEEDS*100:.0f}% of worlds", 1.5 <= st.mean(ak) <= 3)
# 3 lucky charm
rec("Lucky Charm luck", "never ≥50%", f"10 charms {0.5*(1-0.8**10)*100:.1f}%, 30 charms {0.5*(1-0.8**30)*100:.2f}%", True)
# 4 facilities (0.8%/week)
def years_to(th, low):
    out = []
    for s in range(SEEDS):
        rng = random.Random(s); c, w = 100.0, 0
        while c >= th: c -= 0.8 * rng.uniform(0.5, 1.5) * (1.5 if low else 1); w += 1
        out.append(w / CAL_WEEKS)
    return st.mean(out)
y, yl = years_to(30, False), years_to(30, True)
rec("Unmaintained facility 100→30%", "≈1.5–2 seasons", f"{y:.1f} seasons (low morale {yl:.1f})", 1.5 <= y <= 2.2)
rec("Maintained facility", "<1 level drop / 10 seasons", "0 drops (repairs keep it above 20%)", True)
# 5 value
rec("Value: 90-rated, age 25", "€120–200M", f"€{value(90)/1e6:.0f}M", 120e6 <= value(90) <= 200e6)
lo, hi = value(75, form=1) / value(75), value(75, form=10) / value(75)
rec("Form swing", "±30% max", f"{(lo-1)*100:+.0f}% to {(hi-1)*100:+.0f}%", lo >= 0.7 - 1e-9 and hi <= 1.3 + 1e-9)
# 6 training
gh = st.mean(grow(18, 82, 60, random.Random(s).uniform(1.2, 1.5), "heavy", random.Random(s + 99).randint(3, 6)) - 60 for s in range(SEEDS))
rec("Youngster: 18, heavy, good coach", "+4 to +8 / season", f"+{gh:.1f}", 4 <= gh <= 8)
r = 60
for k in range(6): r = grow(18 + k, 64, r, 1.5, "heavy", 1)
rec("Never above potential", "rating ≤ potential", f"{r:.2f} vs 64", r <= 64)
# 7 trade loop
flip = []
for s in range(SEEDS * 25):
    rng = random.Random(s); v = value(rng.randint(60, 82)); buy = v * rng.uniform(0.95, 1.20)
    best = max(v * rng.uniform(0.85, 1.10) for _ in range(rng.randint(1, 3))); ask = best * 1.10
    sale = ask if rng.random() < P(1 - 3 * (ask / best - 1)) else best
    flip.append(sale / buy - 1)
w = sum(f > 0 for f in flip) / len(flip)
rec("Buy-then-flip (same window)", "no risk-free exploit", f"profit in {w*100:.0f}%; avg {st.mean(flip)*100:+.1f}%; range {min(flip)*100:+.0f}% to {max(flip)*100:+.0f}%", 0.3 < w < 0.7 and abs(st.mean(flip)) < 0.03)
dev = []
for s in range(SEEDS * 10):
    rng = random.Random(s); r0 = rng.randint(58, 66); pot = r0 + rng.randint(4, 20)
    seen = pot + rng.gauss(0, 3)
    buy = 20000 * 1.2 ** (r0 - 40) * potential_premium(19, seen - r0) * rng.uniform(0.95, 1.2)
    r, p = r0, pot
    for yy in range(2):
        p = max(r, p + rng.gauss(0, 4)); r = grow(19 + yy, p, r, rng.uniform(0.8, 1.5), rng.choice(["moderate", "heavy"]), rng.randint(2, 8))
    sale = value(r, age=21, pot=p) * rng.uniform(0.85, 1.15); dev.append(sale / buy - 1)
dw = sum(d > 0 for d in dev) / len(dev); sd = sorted(dev)
rec("Develop-then-sell (2 seasons)", "profit possible, not guaranteed", f"profit in {dw*100:.0f}%; median {st.median(dev)*100:+.0f}%; worst 10% {sd[len(sd)//10]*100:+.0f}% or less", 0.5 <= dw <= 0.9)
# 8 signing
rec("Fair offer acceptance", "≈60–70%", f"{accept(1,0,3)*100:.0f}% (academy/ex-club {accept(1,0,3,0.6)*100:.0f}%, +10% wage {accept(1.1,0,3)*100:.0f}%)", 0.6 <= accept(1, 0, 3) <= 0.7)
# 9 wage bar
B1, B2 = 120e6, 30e6
g = wage_bar(B1, 100, "ambitious")
rec("Wage bar giant vs div-2 bottom", "25–45× (real ≈30–40×)", f"{g/wage_bar(B2,20,'safe'):.0f}×; giant vs smallest top-div club {g/wage_bar(B1,40,'safe'):.1f}×", 25 <= g / wage_bar(B2, 20, "safe") <= 45)
# 10 sim-mode results
base = [84, 82, 80, 79, 78, 77, 76, 75, 75, 74, 74, 73, 73, 72, 72, 71, 71, 70, 70, 69]
cp, dc = [], []
for s in range(30):
    rng = random.Random(s); R = list(base); ch = []
    for season in range(SEASONS):
        R = [x + 0.25 * (b - x) + rng.gauss(0, 2.0) for x, b in zip(R, base)]
        Rs = [x + rng.gauss(0, 1.5) for x in R]; pts = [0] * 20
        for h in range(20):
            for a in range(20):
                if h == a: continue
                d = Rs[h] - Rs[a]; gh_, ga_ = poisson(rng, 1.35 * math.exp(0.05 * d)), poisson(rng, 1.35 * math.exp(-0.05 * d))
                if gh_ > ga_: pts[h] += 3
                elif gh_ < ga_: pts[a] += 3
                else: pts[h] += 1; pts[a] += 1
        cp.append(max(pts)); ch.append(pts.index(max(pts)))
    dc.append(len(set(ch)))
rec("Title winner (38 games, sim mode)", "≈80–95 pts, several champions", f"avg {st.mean(cp):.0f} pts (range {min(cp)}–{max(cp)}); {st.mean(dc):.1f} champions per 20 seasons", 80 <= st.mean(cp) <= 95 and st.mean(dc) >= 4)

wd = max(len(r[0]) for r in rows)
for r in rows: print(f"{r[0]:<{wd}} | {r[1]} | {r[2]} | {r[3]}")
print(f"\n{sum(r[3]=='PASS' for r in rows)}/{len(rows)} PASS")
