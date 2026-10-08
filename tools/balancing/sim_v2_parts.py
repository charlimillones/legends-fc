"""Legends FC sim v2 parts (DaiVinci, Oct 8 2026): Carlos's potential/form model + facility upkeep cost."""
import random, statistics as st
clamp=lambda x,a,b:max(a,min(b,x))
# --- Potential (fixed) from academy level ---
def academy_potential(rng, L, protege=False): return clamp(rng.gauss(52+3.5*L, 6) + (5 if protege else 0), 45, 95)
def ceiling(pot, age):                       # age decay of potential
    if age < 30: return pot
    return pot - 1.5*min(age-29, 3) - 3*max(0, age-32)
# --- Growth: training x form; bad form lowers rating ---
def form_factor(form): return clamp(1 + 0.5*(form-6.5), 0, 1.5)
def season(age, pot, r, cq, reg, n, form, weeks=40):
    regm={"light":0.6,"moderate":1.0,"heavy":1.4}[reg]; crowd=1/(1+0.08*(n-1))
    agec=1.3 if age<=21 else 1.0 if age<=25 else 0.5 if age<=29 else 0
    cap=ceiling(pot, age)
    for _ in range(weeks):
        r += 0.08*cq*regm*crowd*agec*form_factor(form)*clamp((cap-r)/10,0,1)
        if form < 5.5: r -= 0.04*(5.5-form)           # consistent bad form
        if r > cap: r -= min(0.1, r-cap)              # age decay pulls rating down
    return r
def prem(age,gap): f=0.5 if age<=21 else 0.25 if age<=23 else 0; return 1.2**(f*max(0,gap))
V=lambda r,age,pot: 20000*1.2**(r-40)*prem(age,pot-r)
out=[]
print("Academy potential by level (mean / top 5% of intake):")
for L in (1,3,5,8,10):
    rng=random.Random(L); ps=sorted(academy_potential(rng,L) for _ in range(5000))
    print(f"  level {L:>2}: {st.mean(ps):.0f} / {ps[int(0.95*len(ps))]:.0f}")
dev=[];flop=0
for s in range(2000):
    rng=random.Random(s); pot=academy_potential(rng, rng.randint(3,9)); r0=clamp(pot-rng.uniform(8,20),50,90)
    seen=pot+rng.gauss(0,3); buy=V(r0,19,seen)*rng.uniform(0.95,1.2); r=r0
    for y in range(2):
        form=clamp(rng.gauss(6.5,0.7),4,8.5)
        r=season(19+y,pot,r,rng.uniform(0.8,1.5),rng.choice(["moderate","heavy"]),rng.randint(2,8),form)
    if r-r0<2: flop+=1
    dev.append(V(r,21,pot)*rng.uniform(0.85,1.15)/buy-1)
sd=sorted(dev)
print(f"Develop-then-sell (fixed potential, form-driven): profit in {sum(d>0 for d in dev)/len(dev)*100:.0f}%, median {st.median(dev)*100:+.0f}%, worst 10% {sd[len(sd)//10]*100:+.0f}%, flops (<+2 in 2 seasons) {flop/len(dev)*100:.0f}%")
r=90
for age in range(29,36): r=season(age,90,r,1.0,"moderate",4,6.5); print(f"  star rated 90 at 29 -> age {age+1}: {r:.0f}", end="")
print()
# --- Facility upkeep cost tied to income ---
for wear in (0.5,0.8):
    costs=[]
    for s in range(200):
        rng=random.Random(s); income=1.0; spent=0
        for f in range(6):
            L=rng.randint(2,8); c=100
            for w in range(52):
                c-=wear*rng.uniform(0.5,1.5)
                if c<60: spent+=(100-c)*0.0002*(L/5)*income; c=100
        costs.append(spent)
    print(f"Upkeep, wear {wear}%/wk, cost per 1% = 0.02% of season income x level/5: {st.mean(costs)*100:.1f}% of season income (max {max(costs)*100:.1f}%)")
