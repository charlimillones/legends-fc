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

- **Step 4a/5: league season in sim mode** (branch `feature/season-sim`, 64 tests):
  - **Fixtures:** round-robin, with every club playing once per round and home games balanced.
  - **Results:** the approved sim-mode model.
  - **League tables:** points, then goal difference, then goals scored.
  - **Formats:** double round-robin; Argentina single round-robin (PROPOSAL).
  - **Balancing check in CI:** title winners in England's first division average 78–97 points; draws are 18–32%.
  - **Speed:** a full season of all 8 leagues runs in well under 2 s.

- **Argentina zone formats** (branch `feature/argentina-zones`, 68 tests):
  - **First division (Apertura and Clausura):**
    - 2 zones of 15, 16 matches per tournament (zone, classic, interzonal);
    - top 8 per zone go into knockouts, single matches, penalties on a draw;
    - the annual-table leader is the League champion;
    - 2 relegated (annual table and averages).
  - **Second division:**
    - 2 zones of 18, home and away;
    - the zone winners play a final for the title and the 1st promotion;
    - the reducido (2nd-8th of each zone, plus the final's loser) decides the 2nd promotion.
  - Promotion and relegation are balanced in every country.
- **No home advantage** (Carlos, Oct 8).
- **Goalkeepers** use the same random age-decay and retirement ranges as outfield players (Carlos, Oct 8).

- **Player development and endless seasons** (branch `feature/development-multiseason`, 78 tests):
  - **Weekly training:** the training formula plus the confirmed form factor and age-decay ceiling. Bad form lowers the rating; potential is never exceeded.
  - **Season cycle:** sim season, training, promotion and relegation, ageing, random retirements, random academy intake of 2-4 per club, at least 16 players per club at all times (any positions), and AI-only squad balance (at least 2 GKs, at most 32, free-agent signings below 16).
  - **Stability:** over 10 seasons the world stays stable (England's first-division average XI goes from 76.4 to 77.5), with 3 different champions. 10 seasons take about 12 s.
  - **Interim, until the systems exist:** AI clubs use default training (moderate regime, average coach, one coach per group); form is neutral; surplus players are released instead of sold.

## In progress
- Step 3b: all national teams (needs the full FIFA country list).

## Next
- **Step 4b:** domestic and continental cups, plus promotion and relegation. Formats need DaiVinci proposals and Carlos's OK.
- **Step 6:** player development (training, form, age decay during the season).

## Open questions for Carlos
- Can players be two-footed? Right now feet are Left or Right only.
- How often does each personality appear? For now they're all equally likely (a PROPOSAL).

## Blockers / waiting on Carlos
- **Unity project:** the editor (6.6) and iOS Build Support are installed. Desktop control expired overnight; one approval is needed to create the URP project and run `unity/bootstrap/Editor/LegendsSmokeTest.cs`.
