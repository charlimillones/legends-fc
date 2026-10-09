using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LegendsFC.Core.Money
{
    public sealed class Currency { public string Code, Name, Symbol; public double PerEur; public int Decimals; }

    /// <summary>data/config/currencies.json. One currency per game, picked at world creation (Oct 9). Internally everything is EUR.</summary>
    public sealed class CurrencyConfig
    {
        public string Default = "EUR";
        public List<Currency> Currencies = new List<Currency>();
        public Currency Get(string code) => Currencies.First(c => c.Code == code);

        /// <summary>Converts an internal EUR amount to the world's display currency.</summary>
        public double FromEur(double eur, string code) => eur * Get(code).PerEur;

        /// <summary>Short display, e.g. "€12.5M", "£840K", "ARS$21.4B".</summary>
        public string Format(double eur, string code)
        {
            var c = Get(code);
            double v = FromEur(eur, code), a = Math.Abs(v);
            string s = a >= 1e9 ? (v / 1e9).ToString("0.#", CultureInfo.InvariantCulture) + "B"
                     : a >= 1e6 ? (v / 1e6).ToString("0.#", CultureInfo.InvariantCulture) + "M"
                     : a >= 1e3 ? (v / 1e3).ToString("0.#", CultureInfo.InvariantCulture) + "K"
                     : v.ToString("0", CultureInfo.InvariantCulture);
            return c.Symbol + s;
        }
    }
}
