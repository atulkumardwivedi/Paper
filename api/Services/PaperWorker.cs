using Npgsql;
using NiftyPremiumTrader.Api.Services;
namespace PaperTrader;
public sealed class PaperWorker(Store store,PaperEngine engine,MarketData market,PaperOptions options,ILogger<PaperWorker> log):BackgroundService {
 public string Status {get;private set;}="Starting";public DateTimeOffset? LastCycle {get;private set;}
 protected override async Task ExecuteAsync(CancellationToken ct){
  while(!ct.IsCancellationRequested){
   try {await Cycle(ct);}catch(OperationCanceledException) when(ct.IsCancellationRequested){break;}catch(Exception e){Status="Worker error: "+e.Message;log.LogError(e,"Paper worker failed; persisted state retained");}
   await Task.Delay(TimeSpan.FromSeconds(Math.Max(1,options.PollSeconds)),ct);
  }
 }
 async Task Cycle(CancellationToken ct){
  if(!options.Enabled||await store.Paused(ct)){Status="Paused; positions remain persisted";return;}
  // One coordinator across API instances; optimistic revisions also protect every write.
  await using var con=await store.Db.OpenConnectionAsync(ct);
  await using var guard=new NpgsqlCommand("SELECT pg_try_advisory_lock(983617420)",con);
  if(!(bool)(await guard.ExecuteScalarAsync(ct))!){Status="Another paper coordinator owns the lease";return;}
  try{
   var today=MarketClock.IndiaDateNow();var states=await store.States(null,ct);
   if(today.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday){
    var expiry=await store.Expiry(today,ct);
    if(expiry==null){Status="Waiting for expiry in trading.option_expiries or stored option candles";}
    else if(!options.ExcludeExpiryDays||expiry!=today){
     var todayStates=await store.States(today,ct);
     foreach(var profile in Profile.All(options)){
      if(todayStates.Any(s=>s.Profile.Id==profile.Id)||states.Any(s=>s.Date<today&&s.Profile.Id==profile.Id&&(s.Active!=null||s.Pending!=null)))continue;
      var s=new Portfolio{Profile=profile,Date=today,Expiry=expiry.Value};await store.Save(s,ct);states.Add(s);
     }
    }else Status="Expiry day excluded by configuration";
   }
   foreach(var state in states.OrderBy(s=>s.Date)){
    try{await engine.Advance(state,ct);await market.UpdateMarks(state,ct);await store.Save(state,ct);}
    catch(Exception e) when(e is not OperationCanceledException){log.LogError(e,"Portfolio {Id} waiting; reload persisted state next cycle",state.Profile.Id);Status=$"Portfolio {state.Profile.Id} error; persisted state retained";}
   }
   LastCycle=DateTimeOffset.UtcNow;if(states.Count>0)Status="Paper portfolios running. "+market.Status;
  }finally{await using var unlock=new NpgsqlCommand("SELECT pg_advisory_unlock(983617420)",con);await unlock.ExecuteNonQueryAsync(CancellationToken.None);}
 }
}
