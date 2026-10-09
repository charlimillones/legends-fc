namespace LegendsFC.Core.Money
{
    /// <summary>
    /// Contract constants. Acceptance itself follows the transfer negotiation rules (Oct 9, Transfers.Pricing):
    /// meeting what he asks closes the deal; below it the chance falls to 0% at 70%.
    /// The old logistic signing formula ("fair offer ≈ 66%") was replaced on Oct 9.
    /// </summary>
    public static class Contracts
    {
        /// <summary>A Businessman asks 20% more (decisions doc).</summary>
        public const double BusinessmanWageFactor = 1.2;
    }
}
