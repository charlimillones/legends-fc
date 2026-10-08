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

- **Step 2b: world models:** country, competition, club, facilities, and players with hidden fields.
- **Step 3: world generation** (branch `feature/world-generation`, 55 tests):
  - **Size:** 222 clubs and 6,660 players, built in about 0.13 s.
  - **Leagues (real sizes):** England 20 + 24, Spain 20 + 22, Brazil 20 + 20, Argentina 30 + 36.
  - **Cup-only countries:** Mexico, USA, Portugal, Netherlands, Uruguay and Colombia, 5 clubs each.
  - **Clubs:** fictional names. Club strength follows a curve per league, and facilities depend on reputation.
  - **Squads:** 25 players plus 5 academy players. Every player has an archetype and a foot (inverted for the 5 exempt archetypes).
  - **Ratings and potential:** attributes are shaped by the archetype and calibrated to the position rating. Youngsters and veterans start a little lower. Academy potential uses the confirmed formula.
  - **Hidden and rare traits:** hidden age decay (confirmed); personalities about 45%; A Keeper 1 in 1,000 outfield players.
  - **Deterministic:** the same seed gives the same world.
  - **Report tool:** `dotnet run --project dotnet/LegendsFC.Tools -- world-report <seed>`.

## In progress
- Step 3b: all national teams (needs the full FIFA country list).

## Next
- Step 4: season calendar and fixtures (leagues, domestic cups, continental cups).

## Open questions for Carlos
- Can players be two-footed? Right now feet are Left or Right only.
- How often does each personality appear? For now they're all equally likely (a PROPOSAL).

## Blockers / waiting on Carlos
- Install on the PC: Git, Unity Hub, Unity 6 LTS with iOS Build Support.
