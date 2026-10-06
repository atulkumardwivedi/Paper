using NiftyPremiumTrader.Api.Models;
using NiftyPremiumTrader.Api.Services;
namespace PaperTrader;
public sealed class MarketData(Store store,BreezeMarketDataClient api,PaperOptions o) {
 public string Status {get;private set;}="Database only";
 public async Task<bool> Ensure(DateOnly date,DateOnly expiry,Contract? k,DateTimeOffset time,bool completed,CancellationToken ct){
  var all=await store.Candles(date,ct);
  if(all.Any(c=>c.CandleTime==time&&(k==null?c.ProductType=="cash":c.ExpiryDate==expiry&&c.Strike==k.Strike&&c.OptionType==k.Side)&&(!completed||c.IsComplete)))return true;
  if(!o.AllowApiDownloads||await store.Blocked(ct))return false;
  var key=$"history:{date}:{(k==null?"cash":k.Key(expiry))}:{time:HHmm}:{completed}";
  if(!await store.Claim(key,TimeSpan.FromSeconds(date==MarketClock.IndiaDateNow()?o.MissingDataRetrySeconds:3600),ct))return false;
  try {
   var candles=k==null?await api.GetIndexDayAsync(date,true,ct):await api.GetOptionDayAsync(date,expiry,k.Strike,k.Side,true,ct);
   await store.Upsert(candles,ct);Status=$"Loaded missing {k?.Side??"cash"} candles";
   return candles.Any(c=>c.CandleTime==time&&(!completed||c.IsComplete));
  }catch(Exception e) when(e is not OperationCanceledException){await Fail(e,ct);return false;}
 }
 async Task Fail(Exception e,CancellationToken ct){
  var wait=e.Message.Contains("Daily",StringComparison.OrdinalIgnoreCase)?TimeSpan.FromHours(24)-MarketClock.ToIndia(DateTimeOffset.UtcNow).TimeOfDay:TimeSpan.FromSeconds(e is ApiBudgetExceededException?60:300);
  Status=e is ApiBudgetExceededException?e.Message:"API unavailable; retry paused for five minutes.";
  await store.Block("GLOBAL",wait,Status,ct);
 }
 public async Task UpdateMarks(Portfolio s,CancellationToken ct){
  if(s.Active is not {} t)return;
  var day=await store.Candles(s.Date,ct);bool market=s.Date==MarketClock.IndiaDateNow()&&MarketClock.IndiaTimeNow()>=new TimeOnly(9,15)&&MarketClock.IndiaTimeNow()<=new TimeOnly(15,30);
  foreach(var leg in t.Legs){
   var k=leg.Contract;var key=k.Key(s.Expiry);var bar=day.LastOrDefault(c=>c.ExpiryDate==s.Expiry&&c.Strike==k.Strike&&c.OptionType==k.Side&&c.IsComplete);
   if(bar!=null)await store.Quote(key,bar.Close,bar.CandleTime.AddMinutes(5),"5-minute database close",ct);
   var q=await store.Quote(key,ct);
   if(market&&o.AllowApiDownloads&&(q==null||DateTimeOffset.UtcNow-q.Value.At>TimeSpan.FromSeconds(o.QuoteRefreshSeconds))&&!await store.Blocked(ct)&&await store.Claim("quote:"+key,TimeSpan.FromSeconds(o.QuoteRefreshSeconds),ct)){
    try{var price=await api.GetOptionQuoteAsync(s.Expiry,k.Strike,k.Side,true,ct);await store.Quote(key,price,DateTimeOffset.UtcNow,"ICICI polled quote",ct);q=await store.Quote(key,ct);}catch(Exception e) when(e is not OperationCanceledException){await Fail(e,ct);}
   }
   if(q!=null){leg.Mark=q.Value.Price;leg.MarkAt=q.Value.At;leg.MarkSource=q.Value.Source;}
  }
  PaperEngine.Mark(s,t);
 }
}
