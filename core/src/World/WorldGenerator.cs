using System;
using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Data;
using LegendsFC.Core.Model;
using LegendsFC.Core.Rules;
using LegendsFC.Core.Util;

namespace LegendsFC.Core.World
{
    /// <summary>
    /// Builds the fictional base world from data + one seed. Same seed and data → identical world (architecture rule 4).
    /// Same rules for every club (rule 5): the user's club is picked after generation.
    /// </summary>
    /// <summary>data/config/protege.json.</summary>
    public sealed class ProtegeConfig
    {
        public double PriceShareBase = 0.5, PriceSharePerLevel = 0.03, PriceShareMax = 0.8;
        public int FirstProtegeBonus = 5;
    }

    public sealed class WorldGenerator
    {
        private readonly GameData _d;
        private readonly WorldGenConfig _c;
        private GameRandom _rng;
        private GameWorld _w;
        private int _clubSeq, _playerSeq;
        private HashSet<string> _usedClubNames;

        public WorldGenerator(GameData data)
        {
            _d = data;
            _c = data.WorldGen;
        }

        /// <param name="currencyCode">The one display currency for this world, chosen by the user (Oct 9). Null = default (EUR).</param>
        public GameWorld Generate(ulong seed, string currencyCode = null)
        {
            string currency = currencyCode ?? _d.Currencies.Default;
            if (!_d.Currencies.Currencies.Any(c => c.Code == currency)) throw new ArgumentException("Unknown currency: " + currency);
            _rng = new GameRandom(seed);
            _w = new GameWorld { Seed = seed, Countries = _d.Countries.ToList(), Competitions = _d.Competitions.ToList(), CurrencyCode = currency };
            _clubSeq = 0; _playerSeq = 0; _usedClubNames = new HashSet<string>();

            foreach (var league in _d.Competitions.Where(c => c.Type == CompetitionType.League).OrderBy(c => c.Id, StringComparer.Ordinal))
            {
                var (best, worst) = _c.ClubRatings.Range(league.Id);
                for (int i = 0; i < league.Teams; i++)
                    AddClub(league.CountryId, league.Level, league.Id, CurveRating(best, worst, i, league.Teams));
            }
            foreach (var country in _d.Countries.Where(c => !c.HasLeague).OrderBy(c => c.Id, StringComparer.Ordinal))
            {
                var (best, worst) = _c.ClubRatings.Range(country.Id);
                for (int i = 0; i < _c.CupOnlyClubsPerCountry; i++)
                    AddClub(country.Id, 0, null, CurveRating(best, worst, i, _c.CupOnlyClubsPerCountry));
            }
            SetUpMoney();
            // Facility managers (own random stream, so adding them didn't change the rest of the world).
            var managers = new GameRandom(seed ^ 0xFAC1_17E5_0000_0001UL);
            foreach (var club in _w.Clubs)
                foreach (var s in club.Facilities.Values) s.Manager = Facilities.FacilityRules.NewManager(_w, club, managers, _d);
            // Coaches (Oct 9): their own random stream too.
            Squad.Coaching.GenerateWorld(_w, new GameRandom(seed ^ 0xC0AC_4E50_0000_0002UL), _d);
            return _w;
        }

        /// <summary>Season-end academy intake for one club (academy level sets potential, confirmed Oct 8).</summary>
        public void AddAcademyIntake(GameWorld world, Club club, GameRandom rng, int count)
        {
            _w = world; _rng = rng;
            _playerSeq = world.Players.Count == 0 ? 0 : world.Players.Max(p => int.Parse(p.Id.Substring(4)));
            // The Youth Academy works at its working level (level × condition, Oct 9).
            int academyLevel = Math.Max(1, (int)Math.Round(Facilities.FacilityRules.WorkingLevel(club.Facilities[Facility.Academy], _d.FacilityRules)));
            // Random positions (confirmed Oct 9: no gap filling).
            for (int i = 0; i < count; i++)
            {
                var pos = Positions.All[_rng.NextInt(0, Positions.All.Length - 1)];
                SetStartingWage(AddPlayer(club, pos, 0, _c.Potential.AcademyAge[0], academyLevel));
            }
        }

        /// <summary>Wage = the expected wage (12% of market value) ± 15%, as at world creation.</summary>
        private void SetStartingWage(Player p)
        {
            int age = _w.SeasonStartYear - p.BirthYear;
            double rating = PositionRating.Base(p.Attributes, p.MainPosition, _d.PositionRatings);
            double value = MarketValue.Eur(rating, 6.5, age, p.Potential, p.ContractEndYear - _w.SeasonStartYear, _d.MarketValue);
            var club = _w.Clubs.First(c => c.Id == p.ClubId);
            p.Wage = (long)Math.Round(Money.Finance.ExpectedWage(value, _w.MoneyKey(club), _d.Finance) * _rng.Uniform(0.85, 1.15));
        }

        /// <summary>Wages from market value (12%, ± a little), fan mood at its normal level, and a starting
        /// balance of half a season's projected income (agreed Oct 9).</summary>
        private void SetUpMoney()
        {
            var f = _d.Finance;
            foreach (var p in _w.Players) SetStartingWage(p);
            foreach (var club in _w.Clubs) club.FanMood = Money.Finance.NormalFanMood(club, f);
            foreach (var group in _w.Clubs.GroupBy(c => _w.MoneyKey(c)))
            {
                var ranked = group.OrderByDescending(c => c.Reputation).ToList();
                for (int i = 0; i < ranked.Count; i++)
                {
                    var club = ranked[i];
                    int home = Season.SeasonSimulator.HomeLeagueMatches(group.Key, ranked.Count, _d);
                    var income = Money.Finance.SeasonIncome(club, group.Key, i + 1, ranked.Count, home, f, _d.FacilityRules);
                    club.Balance = (long)Math.Round(income.Total * f.StartingBalanceSeasons);
                }
            }
        }

        private double CurveRating(double best, double worst, int index, int count)
        {
            if (count <= 1) return best;
            double t = 1.0 - (double)index / (count - 1);           // 1 = best club, 0 = worst
            return worst + (best - worst) * Math.Pow(t, _c.ClubRatings.CurveExponent);
        }

        // ---------------- Clubs ----------------
        private void AddClub(string countryId, int division, string leagueId, double rating)
        {
            var lang = Lang(countryId);
            var club = new Club
            {
                Id = "CLB-" + (++_clubSeq).ToString("D6"),
                Name = UniqueClubName(lang),
                CountryId = countryId,
                Division = division,
                Reputation = Clamp((int)Math.Round((rating - 50) * 2.5), 1, 100),
                PrimaryColor = RandomColor(), SecondaryColor = RandomColor(),
            };
            club.ShortName = club.Name.Length <= 12 ? club.Name : club.Name.Substring(0, 12).TrimEnd();
            club.StadiumCapacity = (int)Math.Round((5000 + club.Reputation * club.Reputation * 6.5) / 500.0) * 500; // PROPOSAL: 5.5k-70k
            foreach (Facility f in Enum.GetValues(typeof(Facility)))
            {
                int level = Clamp((int)Math.Round(1 + club.Reputation / 11.0) + _rng.NextInt(-1, 1), 1, 10);
                club.Facilities[f] = new FacilityState { Level = level, Condition = Math.Round(_rng.Uniform(_c.Facilities.ConditionMin, _c.Facilities.ConditionMax), 1) };
            }
            _w.Clubs.Add(club);
            _w.ClubLeague[club.Id] = leagueId;
            AddSquad(club, rating);
        }

        private string UniqueClubName(LanguageNames lang)
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                string a = Pick(lang.TownA), b = Pick(lang.TownB);
                if (a.EndsWith(" ")) b = char.ToUpperInvariant(b[0]) + b.Substring(1);   // "Loma " + "bonita" → "Loma Bonita"
                string town = a + b;
                string name = Pick(lang.Patterns).Replace("{town}", town);
                if (_usedClubNames.Add(name)) return name;
            }
            throw new InvalidOperationException("Ran out of unique club names; add more name parts.");
        }

        private string RandomColor() => "#" + _rng.NextInt(0, 0xFFFFFF).ToString("X6");

        // ---------------- Players ----------------
        private void AddSquad(Club club, double clubRating)
        {
            var s = _c.Squad;
            foreach (var pos in Positions.All)
            {
                s.Slots.TryGetValue(pos.ToString(), out int count);
                s.StarterSlots.TryGetValue(pos.ToString(), out int starters);
                for (int i = 0; i < count; i++)
                {
                    double penalty = i < starters ? 0 : (i < starters + 1 ? s.BenchPenalty : s.ReservePenalty);
                    int age = Clamp((int)Math.Round(_rng.Gaussian(s.AgeMean, s.AgeSd)), s.AgeMin, s.AgeMax);
                    double agePenalty = Math.Max(0, s.YoungUntilAge - age) * s.YoungPenaltyPerYear + Math.Max(0, age - s.OldFromAge) * s.OldPenaltyPerYear;
                    double rating = clubRating - penalty - agePenalty + _rng.Gaussian(0, s.RatingSpread);
                    AddPlayer(club, pos, rating, age, academyLevel: null);
                }
            }
            int academyLevel = club.Facilities[Facility.Academy].Level;
            for (int i = 0; i < s.AcademyPlayers; i++)
            {
                var pos = Positions.All[_rng.NextInt(0, Positions.All.Length - 1)];
                int age = _rng.NextInt(_c.Potential.AcademyAge[0], _c.Potential.AcademyAge[1]);
                AddPlayer(club, pos, 0, age, academyLevel);
            }
        }

        private Player AddPlayer(Club club, Position main, double targetRating, int age, int? academyLevel, int? forcedPotential = null, string forcedPersonalityId = null)
        {
            var p = new Player { Id = "PLY-" + (++_playerSeq).ToString("D7"), ClubId = club.Id, MainPosition = main };
            bool outfield = Positions.IsOutfield(main);

            // Archetype: A Keeper is 1 in 1,000 outfield players (confirmed Oct 8); otherwise one of the 4 for the group.
            var aKeeper = _d.Archetypes.First(a => a.IsAnyOutfield);
            Archetype arch = outfield && forcedPersonalityId == null && _rng.Chance(aKeeper.SpawnPerOutfieldPlayer ?? 0)
                ? aKeeper
                : PickWeighted(_d.Archetypes.Where(a => a.Group == Positions.GroupOf(main).ToString()).ToList(), a => a.SpawnWeight);
            p.ArchetypeId = arch.Id;

            // Potential and current rating
            int potential = 0;
            if (academyLevel.HasValue)
            {
                var pc = _c.Potential;
                potential = forcedPotential ?? Clamp((int)Math.Round(_rng.Gaussian(pc.AcademyBase + pc.AcademyPerLevel * academyLevel.Value, pc.AcademySd)), pc.AcademyMin, pc.AcademyMax);
                targetRating = potential - _rng.Uniform(pc.AcademyCurrentBelowPotential[0], pc.AcademyCurrentBelowPotential[1]);
            }
            targetRating = Math.Max(25, Math.Min(95, targetRating));
            p.Attributes = BuildAttributes(main, arch, targetRating);
            int current = (int)Math.Round(PositionRating.Base(p.Attributes, main, _d.PositionRatings));
            if (!academyLevel.HasValue) potential = SeniorPotential(current, age);
            p.Potential = Math.Max(current, potential);

            // Identity
            p.NationalityId = _rng.Chance(_c.Nationality.HomeShare) ? club.CountryId : _w.Countries[_rng.NextInt(0, _w.Countries.Count - 1)].Id;
            var lang = Lang(p.NationalityId);
            p.Name = Pick(lang.First) + " " + Pick(lang.Last);
            p.BirthYear = _w.SeasonStartYear - age;
            p.BirthDayOfYear = _rng.NextInt(1, 365);
            p.Foot = PickFoot(main, arch);

            // Hidden age decay (confirmed Oct 8)
            var ad = _c.AgeDecay;
            int gkExtra = main == Position.GK ? ad.GoalkeeperExtraYears : 0;
            p.DeclineStartAge = _rng.NextInt(ad.StartMin, ad.StartMax) + gkExtra;
            // Retirement age on a normal curve, 32-44 centred on 38 (Carlos, Oct 9). The decline start moves earlier if needed (below).
            p.RetireAge = Math.Max(ad.RetireMin, Math.Min(ad.RetireMax, (int)Math.Round(_rng.Gaussian(ad.RetireMean, ad.RetireSd)))) + gkExtra;
            if (p.RetireAge <= age) p.RetireAge = age + _rng.NextInt(1, 2);   // veterans still have a season or two
            if (p.DeclineStartAge > p.RetireAge - ad.MinYearsBetween) p.DeclineStartAge = p.RetireAge - ad.MinYearsBetween;
            p.DeclineAmount = Math.Round(_rng.Uniform(ad.DropMin, ad.DropMax), 1);

            // Personality: about 45% (confirmed range 40-50%); A Keeper always has the A Keeper personality and no other.
            if (forcedPersonalityId != null) p.PersonalityId = forcedPersonalityId;
            else if (arch.LinkedPersonalityId != null) p.PersonalityId = arch.LinkedPersonalityId;
            else if (_rng.Chance(_d.PersonalitySettings.ShareWithPersonality))
                p.PersonalityId = PickWeighted(_d.Personalities.Where(x => !x.ArchetypeOnly).ToList(), x => x.SpawnWeight).Id;

            // Contract length as at world creation (1-5 seasons), counted from the current season.
            p.ContractEndYear = _w.SeasonStartYear + _rng.NextInt(_c.Contracts.EndYearMin, _c.Contracts.EndYearMax) - GameInfo.StartSeasonYear;
            if (academyLevel != null) p.AcademyClubId = club.Id;
            _w.Players.Add(p);
            return p;
        }

        /// <summary>
        /// The yearly protégé (confirmed Oct 9): you choose his position OR his personality, everything else is random,
        /// and his potential is guaranteed to be in the top 50% of what this academy produces.
        /// Returns the player and his price (a standard share of his market value, below market value).
        /// </summary>
        public (Player player, long priceEur) CreateYearlyProtege(GameWorld world, Club club, GameRandom rng, Position? position, string personalityId)
        {
            if ((position == null) == (personalityId == null)) throw new ArgumentException("Choose a position or a personality (one of them).");
            if (personalityId != null && _d.Personalities.Any(x => x.Id == personalityId && x.ArchetypeOnly)) throw new ArgumentException("That personality can't be chosen.");
            _w = world; _rng = rng;
            _playerSeq = world.Players.Count == 0 ? 0 : world.Players.Max(p => int.Parse(p.Id.Substring(4)));
            int level = Math.Max(1, (int)Math.Round(Facilities.FacilityRules.WorkingLevel(club.Facilities[Facility.Academy], _d.FacilityRules)));
            var pc = _c.Potential;
            double median = pc.AcademyBase + pc.AcademyPerLevel * level;
            int potential = Clamp((int)Math.Round(median + Math.Abs(_rng.Gaussian(0, pc.AcademySd))), pc.AcademyMin, pc.AcademyMax); // upper half only
            var pos = position ?? Positions.All[_rng.NextInt(0, Positions.All.Length - 1)];
            var p = AddPlayer(club, pos, 0, pc.AcademyAge[0], level, potential, personalityId);
            SetStartingWage(p);
            double rating = PositionRating.Base(p.Attributes, p.MainPosition, _d.PositionRatings);
            int years = p.ContractEndYear - world.SeasonStartYear;
            double value = MarketValue.Eur(rating, 6.5, pc.AcademyAge[0], p.Potential, years, _d.MarketValue);
            var pr = _d.Protege;
            double share = Math.Min(pr.PriceShareMax, pr.PriceShareBase + pr.PriceSharePerLevel * level);
            return (p, (long)Math.Round(value * share));
        }

        private int SeniorPotential(int current, int age)
        {
            var pc = _c.Potential;
            int gap;
            if (age < 24) gap = (int)Math.Round(Math.Max(0, _rng.Gaussian((24 - age) * pc.YoungGapPerYearUnder24, pc.YoungGapSd)));
            else if (age < 28) gap = _rng.NextInt(0, pc.MidAgeGapMax);
            else gap = 0;
            return Math.Min(99, current + gap);
        }

        private Foot PickFoot(Position main, Archetype arch)
        {
            var side = Positions.SideOf(main);
            if (side == Side.Central) return _rng.Chance(_c.Feet.CentralLeftShare) ? Foot.Left : Foot.Right;
            Foot natural = side == Side.Left ? Foot.Left : Foot.Right;
            Foot inverted = natural == Foot.Left ? Foot.Right : Foot.Left;
            return arch.WrongFootExempt && _rng.Chance(_c.Feet.InvertedOppositeFootShare) ? inverted : natural;
        }

        private static readonly Attr[] OutfieldAttrs = { Attr.Pace, Attr.Acceleration, Attr.Stamina, Attr.Strength, Attr.Dribbling, Attr.Passing, Attr.Crossing, Attr.Shooting, Attr.Heading, Attr.Tackling, Attr.Positioning };
        private static readonly Attr[] GkAttrs = { Attr.Reflexes, Attr.Handling, Attr.RushingOut, Attr.Distribution };

        /// <summary>Archetype shapes the spread (+ start higher, − start lower); then values are shifted so the
        /// main-position rating lands on the target. Backup attributes for the other role stay low (Oct 8).</summary>
        private AttributeSet BuildAttributes(Position main, Archetype arch, double target)
        {
            var ac = _c.Attributes;
            bool gk = main == Position.GK;
            var core = gk ? GkAttrs : OutfieldAttrs;
            var backup = gk ? OutfieldAttrs : GkAttrs;
            var raw = new double[AttributeSet.Count];

            foreach (var a in core)
            {
                string name = a.ToString();
                double v = target + _rng.Gaussian(0, arch.Balanced ? ac.BalancedNoiseSd : ac.NoiseSd);
                if (arch.Plus.Contains(name)) v += _rng.Uniform(ac.PlusMin, ac.PlusMax);
                if (arch.Minus.Contains(name)) v -= _rng.Uniform(ac.MinusMin, ac.MinusMax);
                raw[(int)a] = v;
            }
            int[] backupRange = gk ? ac.OutfieldBackupForGk : (arch.IsAnyOutfield ? ac.AKeeperGkBackup : ac.GkBackupForOutfield);
            foreach (var a in backup) raw[(int)a] = _rng.NextInt(backupRange[0], backupRange[1]);

            var set = new AttributeSet();
            for (int iter = 0; iter < 4; iter++)
            {
                for (int i = 0; i < raw.Length; i++) set[(Attr)i] = Clamp((int)Math.Round(raw[i]), AttributeSet.Min, AttributeSet.Max);
                double diff = target - PositionRating.Base(set, main, _d.PositionRatings);
                if (Math.Abs(diff) < 0.5) break;
                foreach (var a in core) raw[(int)a] += diff;
            }
            return set;
        }

        // ---------------- Helpers ----------------
        private LanguageNames Lang(string countryId) => _d.Names.Languages[_d.Names.CountryLanguage[countryId]];
        private string Pick(List<string> list) => list[_rng.NextInt(0, list.Count - 1)];

        private T PickWeighted<T>(List<T> items, Func<T, double> weight)
        {
            double total = items.Sum(weight), r = _rng.NextDouble() * total;
            foreach (var it in items) { r -= weight(it); if (r < 0) return it; }
            return items[items.Count - 1];
        }

        private static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
    }
}
