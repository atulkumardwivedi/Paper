namespace NiftyPremiumTrader.Api.Options;
public sealed class BreezeOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public string SessionToken { get; set; } = string.Empty;
    public bool UseSessionMaster { get; set; } = true;
    public string StockCode { get; set; } = "NIFTY";
    public string ExchangeCode { get; set; } = "NFO";
    public string IndexExchangeCode { get; set; } = "NSE";
}

