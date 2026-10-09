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

- **Step 7: money, contracts and free agents** (branch `feature/money`, 105 tests):
  - **Currencies:** 22 main currencies (rates of Oct 8, 2026). One per world, chosen by the user at creation; everything is stored in EUR and only displayed in the chosen currency.
  - **Income per season:** TV (half equal, half by position), prize money, gate, sponsors and store (commercial money grows faster with reputation). Wages and about 3% upkeep are paid at season end.
  - **Wages (Oct 9):** stars ask a smaller share of their value; the same player earns less in a poorer league (league wage level).
  - **Calibrated:** the typical club spends about 65% of income on wages in every league (a CI check: 55–75%). A 90-rated star earns about €32M a season in England.
  - **Money never goes below zero** (Oct 9); spending more than the balance is refused.
  - **Fan mood:** results, titles, promotion and relegation, drifting back to normal.
  - **Starting money:** half a season's income (about 9 months of wages).
  - **Contracts (Oct 9):** when a contract runs out, the player and club negotiate a renewal (1–5 years); the outcome decides if he stays. About 43% of expiring players leave (15% not offered, 28% say no at a fair offer).
  - **Free agents (Oct 9):** a player without a club signs for no fee if he accepts the wage (+0.6 for his academy or a former club). The same rule for AI and user.
  - **Squads:** 16–32 players for every club; nobody can sell or release below 16.
  - **Finance report:** `dotnet run --project dotnet/LegendsFC.Tools -- finance-report <seed>`.
  - **Known gap until the transfer market exists:** clubs keep about a third of their income, so cash builds up (England's first division: about 3 seasons of income after 10 seasons). Transfers and facilities will spend it.

- **Step 8: transfer market** (branch `feature/transfer-market`, 123 tests; rules confirmed by Carlos Oct 9):
  - **Negotiations:**
    - two sides: club (fee) and player (wage, years);
    - a live probability, 0–100% in whole numbers;
    - meeting the demand closes the deal;
    - answers by how far off the offer is (lowball → walk away; close → counter or keep talking);
    - patience 3; a 1-week cooldown after a walk-away.
  - **Prices:** fair price = value × importance × form; the asking price is up to 20% more; wage demand at a preferred length; moving to a richer league is a raise.
  - **Selling:** listing brings 0–5 bids; accept, counter, reject or end talks; bidders improve, hold or withdraw; occasional bids for players you haven't listed.
  - **Loans:** until the end of the season, with a fee and a wage split.
  - **Also:** free agents any time, and renewals as negotiations.
  - **AI market:** needs-based buying, AI-to-AI deals, AI loans of young fringe players, surplus listing and releases, a day-by-day 8-week summer window with a ×3 deadline rush.
  - **Balancing:**
    - 170–390 transfers per season;
    - leagues stable over 10 seasons;
    - buy-then-flip makes a profit 38% of the time, average −3% (no risk-free exploit);
    - wages settle near 75% of income.
  - **Tools:** `market-report`, `flip-test` and `wage-room` in `dotnet/LegendsFC.Tools`.
  - **Not yet:** the winter window (needs the weekly calendar), swaps (v1.x), scouting filters (scouting step), and a money sink for rich clubs (facilities step).
- **Facility display names** (branch `feature/facility-names`): Stadium, Training Grounds, Youth Academy, Medical Building, Club Store, Scouting Centre. The ids are unchanged.
- **Unity project** (branch `feature/unity-project`): URP, Unity 6.6, with the core package. The smoke test passes in the editor.

## In progress
- Step 3b: all national teams (needs the full FIFA country list).

## Next
- **Step 4b:** domestic and continental cups, plus promotion and relegation. Formats need DaiVinci proposals and Carlos's OK.
- **Step 9:** facilities (levels, wear, upkeep, Repair all) and the money sink for rich clubs.

## Open questions for Carlos
- Can players be two-footed? Right now feet are Left or Right only.
- How often does each personality appear? For now they're all equally likely (a PROPOSAL).

## Blockers / waiting on Carlos
- **Unity project:** the editor (6.6) and iOS Build Support are installed. Desktop control expired overnight; one approval is needed to create the URP project and run `unity/bootstrap/Editor/LegendsSmokeTest.cs`.
