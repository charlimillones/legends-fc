# Progress

## Done
- **Step 1: repo skeleton.** Core package, dotnet build and tests, CI.
- **Step 2a: rule data and core rules** (40 tests passing):
  - **Data:**
    - 37 archetypes, generated from the confirmed design table;
    - 13 personalities;
    - the related-position map;
    - tuning configs: position-rating weights, out-of-position tiers, probability clamp, Lucky Charm, personality share.
  - **Code:**
    - positions and sides;
    - 15 attributes;
    - position rating;
    - out-of-position tiers (0/5/10/30% plus 5% wrong foot, archetype first);
    - Lucky Charm;
    - probability clamp and 5% display;
    - deterministic random numbers (xoshiro256**);
    - JSON loader shared by Unity and dotnet.
  - **Tests:**
    - data validation (counts, unique IDs, valid tokens, A Keeper linked one-to-one, weights sum to 1);
    - rule values (Lucky Charm 10/18/24.4%, display rounding, every drop tier, the worked rating examples);
    - random-number determinism.

## In progress
- Step 2b: world models done (country, competition, club, facilities, player with hidden fields, JSON round-trip, 43 tests). Waiting on league sizes and the cup-only countries before writing the world data.

## Next
- Step 3: world generation (test world, academy potential, age decay, personalities, A Keeper).

## Open questions for Carlos
- League sizes: "about 80 clubs" across 8 divisions means ~10 clubs per division. Real sizes are 20-24 in the first division, about 170 clubs in total.
- Which 4-6 cup-only countries go in the test world?
- Can players be two-footed? Right now feet are Left or Right only.
- How often does each personality appear? For now they're all equally likely (a PROPOSAL).

## Blockers / waiting on Carlos
- Install on the PC: Git, Unity Hub, Unity 6 LTS with iOS Build Support.
