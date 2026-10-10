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

- **Weekly calendar** (branch `feature/weekly-calendar`, 127 tests):
  - **The season runs week by week (52 weeks):**
    - the summer window in weeks 1–8, day by day;
    - league rounds spread over weeks 9–50, with two rounds in some weeks when a league has more rounds than weeks;
    - the winter window in weeks 27–30, alongside the matches;
    - training in weeks 9–48.
  - **Formats as scripts:** competition formats (round-robin leagues, Argentina's zones, playoffs, final and reducido) are scripts that play round by round. Only the seed and the results are saved, and a half-played season survives a save (tested).
  - **End of season:** `SeasonCycle.EndSeason` handles money, promotion and relegation, the new year, renewals, the academy and free agents.
  - **Wages with the market running:** the typical club now settles near 74% of income (wage bars lowered 10%).

- **Step 9a: facilities** (branch `feature/facilities`, 137 tests; confirmed Oct 9):
  - **Upgrades and repairs:**
    - instant upgrades at fixed prices (€0.5M → €120M);
    - repairs priced from the level (0.2% of the price per 1%);
    - Repair all, or one facility at a time.
  - **Wear and levels:**
    - weekly wear (faster when fans are unhappy);
    - working level from condition;
    - Stadium seats and Club Store income follow the level;
    - level drops below 20%.
  - **Managers:** named managers who retire, with a 5–15% handover loss.
  - **AI clubs:** they repair below 70% and upgrade with spare money (the money sink).
  - **Upkeep:** the flat 3% upkeep was replaced by real repairs.
  - **10-season sim:** levels show wealth (England 8.0 → Argentina's 2nd division 3.3); English top-flight cash after 10 seasons falls from €694M to €441M per club.
  - **Next:** the inbox and the 45 manager messages (step 9b).

- **Step 4b: cups and competitions** (branch `feature/cups`, 157 tests; accepted by Carlos Oct 9, `data/world/cups.json`):
  - **Domestic cups:** English Cup (second division starts, extra time and penalties, final week 51), English League Cup (straight to penalties, European clubs join in the last 32, two-legged semis), Spanish Cup (lower division at home, Super Cup clubs join in the last 32), Brazilian Cup (Champions Cup clubs join in the last 16, two legs from there, two-legged final), Argentine Cup (neutral, straight to penalties).
  - **Super cups:** England, Spain (4 clubs in January), Brazil, Argentina (Apertura v Clausura), Europe, South America (two legs), and the Intercontinental Cup (North v South America, then v Europe).
  - **Continental:** Champions Cup and Europa Cup (12-club league phase, 2 opponents per pot, no same-country games, play-off for 5th-12th, qualifying losers drop down); South American Champions Cup and South American Cup (4 groups of 4, 3rds drop down, qualifying losers drop down); North American Champions Cup (8 clubs).
  - **Places:** from last season's tables and titles; a cup winner already placed passes the spot down the table. Cup-only countries are ranked by a simulated background league.
  - **Engine:** extra time (a third of the 90-minute scoring rate), two-legged ties on aggregate, coefficient over 5 seasons for pots and seeds, prize money paid at season end, honours list. Half-played seasons survive a save (tested at weeks 7, 28 and 45).
  - **Sim (10 seasons):** most matches for one club in one week: 3. English top-flight clubs earn about €11M a season from cups on average (winners up to €65M); English top-flight cash after 10 seasons rises from €441M to €519M per club. Cup-only countries earn 2-3x their small income from continental cups (no wage-share target for them).

- **Step 11: saves** (branch `feature/saves`, 164 tests; settings in `data/config/saves.json`, PROPOSAL):
  - **Worlds:** up to 5 on a device (slots W01-W05), listed most recent first with club, season, week and money; delete frees the slot.
  - **Autosave** after every week through `GameSession.AdvanceWeek` (the UI's "continue" button). The end of the season runs inside it.
  - **Never lose a save:** compressed JSON written to a temp file first, then swapped in; the last 2 saves kept as backups; a damaged or half-written save loads the newest good copy.
  - **Same game after reloading:** the random state is saved too; a career saved and reloaded 5 times (mid-season, at the season's end, early next season) ends identical to one played straight.
  - **Versions:** schema version in every save, step-by-step upgrades for old saves, a clear message for saves from a newer game.
  - **Size:** about 1 MB per world after a season (0.5 MB new), 1.6 MB after 10 seasons. Save or load takes about 0.1-0.2 s on a PC.
  - **Confirmed by Carlos (Oct 9):** 5 worlds, autosave every week, 2 backups.
- **Retirement (Carlos, Oct 9):** retirement age on a normal curve from 32 to 44 centred on 38 (32 as rare as 44, about 0.3% each; 11% play past 40). A free agent who goes 2 full seasons without a club retires: free agents now level off at about 1,550 instead of growing past 4,000.

- **Step 9b: inbox and manager messages** (branch `feature/inbox`, 173 tests; wordings in `data/text/manager-messages.json`, thresholds in `data/config/inbox.json`, PROPOSAL):
  - **Rules:** at most 2 manager messages a week, by priority (health, then damage and maintenance, then contracts and rival interest, then the rest); lower ones wait up to 2 weeks, then are dropped; no wording twice in a season; a manager never sends the same trigger twice in a row (the monthly report is exempt: it comes 13 times a season).
  - **27 live triggers:** facility upgrades, condition below 60% / 30%, repairs, level drops, manager retiring, new manager; sell-outs, low and record attendance (new: attendance per home league match), big home cup matches; attribute rises, great training weeks, stalled players; intake day, top prospect, youngster ready, youngster released, protégé; big wins, star signings, bad runs, trophies, monthly store income; scouting upgrade, rival interest. The other 18 switch on with injuries, coaches, scouts, kits, events, lineups and per-player regimes.
  - **Game actions** in `GameSession`: upgrade a facility, repair one, repair all (each sends its manager message).
  - **Safe:** wording choice uses its own random stream, so the inbox never changes the game (tested); never reveals archetypes, hidden personalities or potential (tested); saved with the world.

- **Squad and tactics** (branch `feature/squad-tactics`, 187 tests; agreed with Carlos Oct 9, data in `config/squad.json`, `config/match-events.json`, `rules/injuries.json`, `rules/discipline.json`):
  - **Teams:** 11 + 9 subs, 5 substitutions, 8 formations (wing-backs use full-back ratings), mentality (5 steps), captain and set-piece takers, 5 saved lineups, auto-pick. AI clubs pick the formation that fits their squad and the best 11, counting energy; mentality by relative strength.
  - **Sim results from real lineups:** attack (midfield + attack slots, forwards double) v defence (keeper double, defence, midfield); out-of-position players and tired players hurt. Strength factor retuned 0.05 -> 0.056 (English champions about 80 points).
  - **Match events:** scorers (position x shooting), assists (passing and crossing), yellows about 1.8 and reds about 0.09 per team per match, injuries about 0.36 per team per match, 3-5 substitutions, 1-10 ratings (average 6.5), player of the match.
  - **Energy (Carlos):** distance covered, passes and shots (not goals or assists), less with stamina; weekly rest by regime (light = full, moderate +40, heavy +25); Workhorse x1.25, Socialite x0.85.
  - **Injuries:** 10 types (knock to cruciate ligament), more likely when tired, Healthy x0.5, Thoroughbred x1.5, Lucky Charm; the Medical Building shortens them (level 1 about 4.3 weeks average, level 10 about 2.6).
  - **Suspensions by each competition's rules** (England 5/10/15 with deadlines, Spain every 5, Brazil every 3, Argentina every 5 per tournament, UEFA 4-6-8-10, CONMEBOL every 3; English reds cover all English competitions, European reds carry between European cups). Unconfirmed cups marked PROPOSAL.
  - **Stats and form:** appearances, starts, minutes, goals, assists, clean sheets, cards, ratings, player of the match, per competition and season (older seasons merged per club); form = last 10 ratings drives development and prices. Rating vs team average softened to 0.02 per point so young players aren't held back (league quality stays stable over 10 seasons).
  - **Inbox:** 8 more triggers live (injuries, serious injuries, recovery ahead, back in training, too many injured, injury risk, heavy regime too long, academy debut): 35 of 45.
  - **Speed:** a full season with every cup in about 5-6 s on the cloud machine (was about 3 s); 10 seasons in about 60 s.

- **Coaches** (branch `feature/coaches`, NOT merged: waiting for Carlos; `config/coaches.json`, PROPOSAL numbers):
  - 4 types (goalkeeper, defence, midfield, forward), a rating, a contract of 1-4 seasons and a one-time price (no wages): EUR 25K x 1.1^(rating - 40) per season (rating 60 = 168K, 80 = 1.1M); ex-players 30% cheaper at their old clubs.
  - Limit = 3 x Training Grounds working level (level 1 = 3, level 10 = 30).
  - Growth: with a coach (0.5 + rating/100) x crowd; without one 0.6 (natural development). A 50-rated coach with 7 players = the balance before coaches.
  - World: every club starts with a coach per group (better clubs, better coaches), a free pool of 150; each season coaches retire, contracts end, 15% of retiring players become coaches, rarely a former player joins his old club free.
  - AI: a coach per group, more for big clubs (6 at reputation 60+, 8 at 80+), at most 3% of their money per coach.
  - User: hire, renew, release, assign players (a coach trains only his group), auto-assign. Inbox: coach signed, contract ending (week 40), group too big (7+): 38 of 45 triggers live.
  - League quality over 10 seasons: English top-flight average XI 77.1 -> 76.0.

## In progress
- Step 3b: all national teams (needs the full FIFA country list).

## Next
- Inbox and the 45 manager messages (step 9b); match attendance per game (Stadium messages, cup gate split).
- First playable test build in Unity.

## Open questions for Carlos
- Can players be two-footed? Right now feet are Left or Right only.
- How often does each personality appear? For now they're all equally likely (a PROPOSAL).

## Blockers / waiting on Carlos
- **Unity project:** the editor (6.6) and iOS Build Support are installed. Desktop control expired overnight; one approval is needed to create the URP project and run `unity/bootstrap/Editor/LegendsSmokeTest.cs`.
