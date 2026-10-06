using Npgsql;
using PaperTrader;
namespace NiftyPremiumTrader.Api.Services;
public sealed class ApiBudgetExceededException(string message):Exception(message) {}
public sealed class ApiBudgetService(Store store,PaperOptions o) {
 public async Task ConsumeAsync(int count,bool livePriority,CancellationToken ct){
  if(!o.AllowApiDownloads||await store.Blocked(ct))throw new ApiBudgetExceededException("API disabled or cooling down; database processing continues.");
  await using var con=await store.Db.OpenConnectionAsync(ct);await using var tx=await con.BeginTransactionAsync(ct);
  await using(var c=new NpgsqlCommand("SELECT pg_advisory_xact_lock(741938521)",con,tx))await c.ExecuteNonQueryAsync(ct);
  await using(var c=new NpgsqlCommand("DELETE FROM trading.api_minute_calls WHERE reserved_at<=now()-interval '60 seconds'",con,tx))await c.ExecuteNonQueryAsync(ct);
  await using(var c=new NpgsqlCommand("SELECT coalesce(sum(call_count),0) FROM trading.api_minute_calls",con,tx))
   if(Convert.ToInt32(await c.ExecuteScalarAsync(ct))+count>o.PerMinuteApiLimit)throw new ApiBudgetExceededException("Minute API limit reached.");
  await using(var c=new NpgsqlCommand("INSERT INTO trading.api_daily_budget(usage_date,used_count) VALUES(@d,@n) ON CONFLICT(usage_date) DO UPDATE SET used_count=trading.api_daily_budget.used_count+@n,updated_at=now() WHERE trading.api_daily_budget.used_count+@n<=@max RETURNING used_count",con,tx)){
   c.Parameters.AddWithValue("d",MarketClock.IndiaDateNow());c.Parameters.AddWithValue("n",count);c.Parameters.AddWithValue("max",o.DailyApiLimit-o.ReservedApiCalls);
   if(await c.ExecuteScalarAsync(ct)==null)throw new ApiBudgetExceededException("Daily API budget reached.");
  }
  await using(var c=new NpgsqlCommand("INSERT INTO trading.api_minute_calls VALUES(now(),@n)",con,tx)){c.Parameters.AddWithValue("n",count);await c.ExecuteNonQueryAsync(ct);}await tx.CommitAsync(ct);
 }
}
