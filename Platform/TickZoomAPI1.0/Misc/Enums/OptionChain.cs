namespace TickZoom.Api
{
    public enum OptionChain
    {
        /// <summary>
        /// Creates an extrapolated trade price and size by watching Level II data changes. 
        /// This is appropriate for Forex since it never has actual trade data.
        /// </summary>
        None,
        /// <summary>
        /// Collect all strike prices and all expirations.
        /// </summary>
        Complete
    }
}