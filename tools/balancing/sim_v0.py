"""Legends FC balancing sim v0 (DaiVinci). Tests PROPOSAL v0 formulas.
Run: python3 sim_v0.py  -> prints a results table. Deterministic seeds.
"""
import math, random, statistics as st

SEEDS, SEASONS, WEEKS = 200, 20, 38       # match weeks per season
CAL_WEEKS = 52                              # calendar weeks (facility decay)

clamp = lambda x, a, b: max(a, min(b, x))
P = lambda x: clamp(x, 0.05, 0.95)          # probability clamp 5-95%
logistic = lambda z: 1 / (1 + math.exp(-z))
results = []
def rec(check, target, found, ok): results.append((check, target, found, "PASS" if ok else "FAIL"))

# ---------- 1. Random events ----------
def season_events(rng, bal):
    n, p = 0, 3 / WEEKS
    out = []
    for w in range(WEEKS):
        left = WEEKS - w
        need = 2 - n
        if n < 4 and (rng.random() < p or need >= left):   # floor 2, cap 4
            good_p = clamp(0.5 + 0.15 * (bal['bad'] - bal['good']), 0.2, 0.8)
            g = rng.random() < good_p
            out.append(g); n += 1
    return out
counts, good_shares = [], []
for s in range(SEEDS):
    rng = random.Random(s); hist = []
    for season in range(SEASONS):
        last2 = hist[-2:]
        bal = {'good': sum(sum(x) for x in last2), 'bad': sum(len(x) - sum(x) for x in last2)}
        ev = season_events(rng, bal); hist.append(ev); counts.append(len(ev))
    for i in range(0, SEASONS - 4, 5):
        blk = [e for x in hist[i:i + 5] for e in x]
        good_shares.append(sum(blk) / len(blk))
rec("Events per season", "always 2–4, mean ≈3",
    f"min {min(counts)}, max {max(counts)}, mean {st.mean(counts):.2f}",
    min(counts) >= 2 and max(counts) <= 4 and 2.7 <= st.mean(counts) <= 3.3)
gs_in = sum(0.4 <= g <= 0.6 for g in good_shares) / len(good_shares)
rec("Good share over 5 seasons", "40–60%",
    f"avg {st.mean(good_shares)*100:.0f}%, {gs_in*100:.0f}% of 5-season blocks inside 40–60%", gs_in >= 0.8)

# ---------- 2. Personalities and A Keeper ----------
rng = random.Random(1)
outfield = 80 * 22 + 25 * 22                 # test world: ~80 league clubs + ~5 cup-only countries x5 clubs
players = outfield + 80 * 3 + 25 * 3
pers = sum(rng.random() < 0.45 for _ in range(players)) / players
akeepers = []
for s in range(SEEDS):
    r_ = random.Random(s); akeepers.append(sum(r_.random() < 1 / 1000 for _ in range(outfield)))
rec("Players with a personality", "40–50%", f"{pers*100:.1f}%", 0.40 <= pers <= 0.50)
rec("A Keeper in test world", "≈2 (1 in 1,000 outfield)",
    f"mean {st.mean(akeepers):.1f}, 0 in {sum(a==0 for a in akeepers)/SEEDS*100:.0f}% of worlds", 1 <= st.mean(akeepers) <= 3)

# ---------- 3. Lucky Charm ----------
luck = [0.5 * (1 - 0.8 ** n) for n in range(1, 31)]
rec("Lucky Charm luck", "never ≥50%", f"1:{luck[0]*100:.0f}% 3:{luck[2]*100:.1f}% 10:{luck[9]*100:.1f}% 30:{luck[29]*100:.2f}%", max(luck) < 0.5)

# ---------- 4. Facilities ----------
def decay_to(threshold, weekly, low_morale=False, seed=0):
    rng = random.Random(seed); c, w = 100.0, 0
    while c >= threshold:
        c -= weekly * rng.uniform(0.5, 1.5) * (1.5 if low_morale else 1); w += 1
    return w / CAL_WEEKS
for wk, tag in [(0.4, "v0 0.4%/wk"), (0.8, "fix 0.8%/wk")]:
    yrs = st.mean(decay_to(30, wk, seed=s) for s in range(SEEDS))
    yrs_low = st.mean(decay_to(30, wk, True, s) for s in range(SEEDS))
    rec(f"Unmaintained facility 100→30% ({tag})", "≈1.5–2 seasons",
        f"{yrs:.1f} seasons (low morale {yrs_low:.1f})", 1.5 <= yrs <= 2.2)
# maintained: repair whenever < 60%; level drop only if <20% at season end
drops = 0
for s in range(SEEDS):
    rng = random.Random(s); c = 100
    for season in range(10):
        for w in range(CAL_WEEKS):
            c -= 0.8 * rng.uniform(0.5, 1.5)
            if c < 60: c = 100
        if c < 20 and rng.random() < 0.25: drops += 1
rec("Maintained facility", "<1 level drop / 10 seasons", f"{drops/SEEDS:.2f} drops per 10 seasons", drops / SEEDS < 1)

# ---------- 5. Market value ----------
def value(r, form=6.5, age=25, pot=None, years=3, form_cap=1.4):
    pot = r if pot is None else pot
    F = clamp(1 + 0.15 * (form - 6.5), 0.7, form_cap)
    A = 1.0 if age <= 29 else 0.85 ** (age - 29)
    Pm = 1 + 0.04 * max(0, pot - r) if age <= 23 else 1
    C = min(1, 0.4 + 0.2 * years)
    return 20000 * 1.2 ** (r - 40) * F * A * Pm * C
v90 = value(90)
rec("Value: 90-rated, age 25", "€120–200M", f"€{v90/1e6:.0f}M", 120e6 <= v90 <= 200e6)
lo, hi = value(75, form=1) / value(75), value(75, form=10) / value(75)
rec("Form swing (v0 cap 1.4)", "±30% max", f"{(lo-1)*100:+.0f}% to {(hi-1)*100:+.0f}%", lo >= 0.7 and hi <= 1.3)
hi2 = value(75, form=10, form_cap=1.3) / value(75)
rec("Form swing (fix cap 1.3)", "±30% max", f"{(lo-1)*100:+.0f}% to {(hi2-1)*100:+.0f}%", hi2 <= 1.3 + 1e-9)

# ---------- 6. Training growth ----------
def season_growth(age, pot, r0, coach_q, regime, n_on_coach, fac_level=5, cond=100, weeks=40, pers=1.0):
    r = r0
    reg = {"light": 0.6, "moderate": 1.0, "heavy": 1.4}[regime]
    crowd = 1 / (1 + 0.08 * (n_on_coach - 1))
    agec = 1.3 if age <= 21 else 1.0 if age <= 25 else 0.5 if age <= 29 else 0
    fac = 0.7 + 0.06 * fac_level * (0.6 + 0.4 * cond / 100)
    for _ in range(weeks):
        room = clamp((pot - r) / 10, 0, 1)
        r += 0.08 * coach_q * reg * crowd * 1.0 * pers * agec * room * fac   # Arch avg ≈1 across attributes
    return r - r0
g_heavy = st.mean(season_growth(18, 82, 60, random.Random(s).uniform(1.2, 1.5), "heavy", random.Random(s+99).randint(3, 6)) for s in range(SEEDS))
rec("Youngster: 18, heavy, good coach", "+4 to +8 / season", f"+{g_heavy:.1f}", 4 <= g_heavy <= 8)
g_light = season_growth(18, 82, 60, 1.0, "light", 8)
rec("Youngster: 18, light, average coach, crowded", "clearly slower", f"+{g_light:.1f}", g_light < g_heavy * 0.6)
near = 60; peak = 64
for season in range(6): near += season_growth(18 + season, peak, near, 1.5, "heavy", 1)
rec("Never above potential", "rating ≤ potential", f"after 6 seasons: {near:.2f} vs pot {peak}", near <= peak)

# ---------- 7. Trade loop (needs bid/ask models: PROPOSAL) ----------
flip = []
for s in range(SEEDS * 25):
    rng = random.Random(s)
    v = value(rng.randint(60, 82))
    buy = v * rng.uniform(0.95, 1.20)                  # PROPOSAL: AI seller asks 0.95-1.20x value
    nbids = rng.randint(1, 4)                           # clubs that need him and can afford
    best = max(v * rng.uniform(0.85, 1.15) for _ in range(nbids))  # PROPOSAL: AI bid 0.85-1.15x value
    ask = best * 1.10                                   # user counters +10%
    sale = ask if rng.random() < P(1 - 3 * (ask / best - 1)) else best
    flip.append(sale / buy - 1)
win = sum(f > 0 for f in flip) / len(flip)
rec("Buy-then-flip (same window)", "no risk-free exploit",
    f"profit in {win*100:.0f}% of flips; avg {st.mean(flip)*100:+.1f}%; best {max(flip)*100:+.0f}%, worst {min(flip)*100:+.0f}%",
    0.05 < win < 0.95)
dev = []
for s in range(SEEDS * 5):
    rng = random.Random(s); r0 = rng.randint(58, 66); pot = r0 + rng.randint(4, 20)
    buy = value(r0, age=19, pot=pot) * rng.uniform(0.95, 1.2)
    r = r0
    for y in range(2): r += season_growth(19 + y, pot, r, rng.uniform(0.8, 1.5), rng.choice(["moderate", "heavy"]), rng.randint(2, 8))
    sale = value(r, age=21, pot=pot) * rng.uniform(0.85, 1.15)
    dev.append(sale / buy - 1)
rec("Develop-then-sell (2 seasons)", "profit possible, not guaranteed",
    f"profit in {sum(d>0 for d in dev)/len(dev)*100:.0f}%; median {st.median(dev)*100:+.0f}%", 0.05 < sum(d > 0 for d in dev) / len(dev) < 0.99)

# ---------- 8. Signing acceptance ----------
par = P(logistic(0 + 0 + 0.05 * 3))
rec("Fair offer (expected wage, fair fee, 3 yrs), v0", "≈60–70% (new target)", f"{par*100:.0f}%", 0.6 <= par <= 0.7)
par2 = P(logistic(0.5 + 0.05 * 3))
rec("Fair offer, fix: +0.5 base", "≈60–70%", f"{par2*100:.0f}% (academy/ex-club {P(logistic(0.5+0.15+0.6))*100:.0f}%)", 0.6 <= par2 <= 0.7)

# ---------- 9. Wage bar ----------
obj = {"safe": 0.95, "standard": 1.0, "ambitious": 1.15}
def bar_v0(base, rep, o): return base * (0.5 + rep / 100) * obj[o]
def bar_fix(base, rep, o): return base * 1.025 ** (rep - 50) * obj[o]
B1, B2 = 120e6, 15e6                                    # PROPOSAL: England base, div1 and div2 (8x)
for f, tag in [(bar_v0, "v0"), (bar_fix, "fix 1.025^(rep−50)")]:
    giant, small1, bottom2 = f(B1, 100, "ambitious"), f(B1, 40, "safe"), f(B2, 20, "safe")
    rec(f"Wage bar giant vs div-2 bottom ({tag})", "15–30×", f"{giant/bottom2:.0f}×; giant vs smallest top-div club {giant/small1:.1f}×",
        15 <= giant / bottom2 <= 30 and giant / small1 >= 4)

# ---------- 10. League (sim-mode result model: PROPOSAL) ----------
def poisson(rng, lam):
    L, k, p = math.exp(-lam), 0, 1.0
    while True:
        p *= rng.random()
        if p < L: return k
        k += 1
champ_pts, champs = [], []
for s in range(SEEDS // 4):
    rng = random.Random(s)
    base = [84, 82, 80, 79, 78, 77, 76, 75, 75, 74, 74, 73, 73, 72, 72, 71, 71, 70, 70, 69]
    for season in range(SEASONS):
        R = [b + rng.gauss(0, 1.5) for b in base]
        pts = [0] * 20
        for h in range(20):
            for a in range(20):
                if h == a: continue
                d = R[h] - R[a]
                gh, ga = poisson(rng, 1.35 * math.exp(0.07 * d)), poisson(rng, 1.35 * math.exp(-0.07 * d))
                if gh > ga: pts[h] += 3
                elif gh < ga: pts[a] += 3
                else: pts[h] += 1; pts[a] += 1
        champ_pts.append(max(pts)); champs.append(pts.index(max(pts)))
distinct = len(set(champs[:SEASONS]))
rec("Title winner points (38 games)", "≈80–95, not same champ every year",
    f"avg {st.mean(champ_pts):.0f} (range {min(champ_pts)}–{max(champ_pts)}); {distinct} different champions in first 20 seasons",
    78 <= st.mean(champ_pts) <= 95 and distinct >= 3)

w = max(len(r[0]) for r in results)
print(f"{'Check':<{w}} | Target | Found | Result")
for r in results: print(f"{r[0]:<{w}} | {r[1]} | {r[2]} | {r[3]}")
