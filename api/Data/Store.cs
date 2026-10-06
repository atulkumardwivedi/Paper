using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using NiftyPremiumTrader.Api.Models;
namespace PaperTrader;
public sealed class Store(NpgsqlDataSource db) {
 public static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){IgnoreReadOnlyProperties=true};
 private readonly System.Collections.Concurrent.ConcurrentDictionary<DateOnly,(DateTimeOffset At,List<OptionCandle> Rows)> cache=new();
 public NpgsqlDataSource Db=>db;
 public async Task Initialize(CancellationToken ct) {await using var c=db.CreateCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Sql/schema.sql"),ct));await c.ExecuteNonQueryAsync(ct);}
 public async Task<List<Portfolio>> States(DateOnly? date,CancellationToken ct) {
  await using var c=db.CreateCommand(date.HasValue?"SELECT state::text FROM paper.portfolios WHERE session_date=@d ORDER BY profile_id":"SELECT state::text FROM paper.portfolios WHERE NOT COALESCE((state->>'dayComplete')::boolean,false) ORDER BY session_date,profile_id");
  if(date.HasValue)c.Parameters.AddWithValue("d",date.Value);var a=new List<Portfolio>();await using var r=await c.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))a.Add(JsonSerializer.Deserialize<Portfolio>(r.GetString(0),Json)!);return a;
 }
 public async Task Save(Portfolio s,CancellationToken ct) {
  s.UpdatedAt=DateTimeOffset.UtcNow;long expected=s.Revision;s.Revision++;
  await using var c=db.CreateCommand("INSERT INTO paper.portfolios(profile_id,session_date,state) VALUES(@p,@d,@s) ON CONFLICT(profile_id,session_date) DO UPDATE SET state=excluded.state,updated_at=now() WHERE (paper.portfolios.state->>'revision')::bigint=@expected");
  c.Parameters.AddWithValue("expected",expected);c.Parameters.AddWithValue("p",s.Profile.Id);c.Parameters.AddWithValue("d",s.Date);c.Parameters.Add("s",NpgsqlDbType.Jsonb).Value=JsonSerializer.Serialize(s,Json);if(await c.ExecuteNonQueryAsync(ct)!=1)throw new InvalidOperationException("Concurrent portfolio update rejected; state will reload.");
 }
 public async Task<bool> Paused(CancellationToken ct){await using var c=db.CreateCommand("SELECT paused FROM paper.control WHERE id=1");return (bool)(await c.ExecuteScalarAsync(ct))!;}
 public async Task Pause(bool v,CancellationToken ct){await using var c=db.CreateCommand("UPDATE paper.control SET paused=@v WHERE id=1");c.Parameters.AddWithValue("v",v);await c.ExecuteNonQueryAsync(ct);}
 public async Task<List<DateOnly>> Dates(CancellationToken ct){await using var c=db.CreateCommand("SELECT DISTINCT session_date FROM paper.portfolios ORDER BY 1 DESC");await using var r=await c.ExecuteReaderAsync(ct);var a=new List<DateOnly>();while(await r.ReadAsync(ct))a.Add(r.GetFieldValue<DateOnly>(0));return a;}
 public async Task<DateOnly?> Expiry(DateOnly d,CancellationToken ct){
  await using var c=db.CreateCommand("SELECT min(expiry_date) FROM (SELECT expiry_date FROM trading.option_expiries WHERE expiry_date>=@d UNION SELECT expiry_date FROM trading.premium_candles_5m WHERE session_date=@d AND stock_code='NIFTY' AND expiry_date>=@d) x");c.Parameters.AddWithValue("d",d);var v=await c.ExecuteScalarAsync(ct);return v is DateTime dt?DateOnly.FromDateTime(dt):v is DateOnly a?a:null;
 }
 public async Task<List<OptionCandle>> Candles(DateOnly d,CancellationToken ct){
  if(cache.TryGetValue(d,out var cached)&&DateTimeOffset.UtcNow-cached.At<TimeSpan.FromSeconds(2))return cached.Rows;
  await using var c=db.CreateCommand("SELECT candle_time,product_type,expiry_date,strike,option_type,open_price,high_price,low_price,close_price,volume,open_interest,is_complete,delta FROM trading.premium_candles_5m WHERE session_date=@d AND stock_code='NIFTY' ORDER BY candle_time");c.Parameters.AddWithValue("d",d);var a=new List<OptionCandle>();await using var r=await c.ExecuteReaderAsync(ct);
  while(await r.ReadAsync(ct))a.Add(new(){SessionDate=d,CandleTime=new DateTimeOffset(r.GetDateTime(0),TimeSpan.Zero),ProductType=r.GetString(1),ExpiryDate=r.IsDBNull(2)?null:r.GetFieldValue<DateOnly>(2),Strike=r.IsDBNull(3)?null:r.GetDecimal(3),OptionType=r.IsDBNull(4)?null:r.GetString(4),Open=r.GetDecimal(5),High=r.GetDecimal(6),Low=r.GetDecimal(7),Close=r.GetDecimal(8),Volume=r.GetInt64(9),OpenInterest=r.GetInt64(10),IsComplete=r.GetBoolean(11),Delta=r.IsDBNull(12)?null:r.GetDecimal(12)});cache[d]=(DateTimeOffset.UtcNow,a);if(cache.Count>8)foreach(var key in cache.Keys.Where(k=>k!=d).Order().Take(cache.Count-8))cache.TryRemove(key,out _);return a;
 }
 public async Task Upsert(IEnumerable<OptionCandle> candles,CancellationToken ct){
  await using var con=await db.OpenConnectionAsync(ct);await using var tx=await con.BeginTransactionAsync(ct);
  foreach(var x in candles.Where(x=>x.Open>0&&x.Close>0&&x.Low>0&&x.High>=Math.Max(x.Open,x.Close)&&x.Low<=Math.Min(x.Open,x.Close))){
   string key=x.ProductType=="cash"?"NSE|CASH|NIFTY":new Contract(x.Strike!.Value,x.OptionType!).Key(x.ExpiryDate!.Value);
   await using var c=new NpgsqlCommand("""
    INSERT INTO trading.premium_candles_5m(session_date,candle_time,contract_key,stock_code,exchange_code,product_type,expiry_date,strike,option_type,open_price,high_price,low_price,close_price,volume,open_interest,is_complete,data_source)
    VALUES(@d,@t,@k,'NIFTY',@ex,@p,@e,@s,@o,@op,@hi,@lo,@cl,@v,@oi,@done,'BREEZE')
    ON CONFLICT(session_date,contract_key,candle_time) DO UPDATE SET
    open_price=excluded.open_price,high_price=excluded.high_price,low_price=excluded.low_price,close_price=excluded.close_price,
    volume=excluded.volume,open_interest=excluded.open_interest,is_complete=excluded.is_complete,updated_at=now()
    WHERE NOT trading.premium_candles_5m.is_complete
    """,con,tx);
   c.Parameters.AddWithValue("d",x.SessionDate);c.Parameters.AddWithValue("t",x.CandleTime.ToUniversalTime());c.Parameters.AddWithValue("k",key);c.Parameters.AddWithValue("ex",x.ProductType=="cash"?"NSE":"NFO");c.Parameters.AddWithValue("p",x.ProductType);
   c.Parameters.Add("e",NpgsqlDbType.Date).Value=(object?)x.ExpiryDate??DBNull.Value;c.Parameters.Add("s",NpgsqlDbType.Numeric).Value=(object?)x.Strike??DBNull.Value;c.Parameters.Add("o",NpgsqlDbType.Text).Value=(object?)x.OptionType??DBNull.Value;
   c.Parameters.AddWithValue("op",x.Open);c.Parameters.AddWithValue("hi",x.High);c.Parameters.AddWithValue("lo",x.Low);c.Parameters.AddWithValue("cl",x.Close);c.Parameters.AddWithValue("v",x.Volume);c.Parameters.AddWithValue("oi",x.OpenInterest);c.Parameters.AddWithValue("done",x.IsComplete);await c.ExecuteNonQueryAsync(ct);
  }await tx.CommitAsync(ct);cache.Clear();
 }
 public async Task<bool> Claim(string key,TimeSpan wait,CancellationToken ct){await using var c=db.CreateCommand("INSERT INTO paper.requests(request_key,retry_after) VALUES(@k,now()+@w) ON CONFLICT(request_key) DO UPDATE SET retry_after=excluded.retry_after WHERE paper.requests.retry_after<=now() RETURNING request_key");c.Parameters.AddWithValue("k",key);c.Parameters.AddWithValue("w",wait);return await c.ExecuteScalarAsync(ct)!=null;}
 public async Task Block(string key,TimeSpan wait,string message,CancellationToken ct){await using var c=db.CreateCommand("INSERT INTO paper.requests(request_key,retry_after,message) VALUES(@k,now()+@w,@m) ON CONFLICT(request_key) DO UPDATE SET retry_after=excluded.retry_after,message=excluded.message");c.Parameters.AddWithValue("k",key);c.Parameters.AddWithValue("w",wait);c.Parameters.AddWithValue("m",message);await c.ExecuteNonQueryAsync(ct);}
 public async Task<bool> Blocked(CancellationToken ct){await using var c=db.CreateCommand("SELECT EXISTS(SELECT 1 FROM paper.requests WHERE request_key='GLOBAL' AND retry_after>now())");return (bool)(await c.ExecuteScalarAsync(ct))!;}
 public async Task<(decimal Price,DateTimeOffset At,string Source)?> Quote(string key,CancellationToken ct){await using var c=db.CreateCommand("SELECT price,observed_at,source FROM paper.quotes WHERE contract_key=@k");c.Parameters.AddWithValue("k",key);await using var r=await c.ExecuteReaderAsync(ct);return await r.ReadAsync(ct)?(r.GetDecimal(0),new DateTimeOffset(r.GetDateTime(1),TimeSpan.Zero),r.GetString(2)):null;}
 public async Task Quote(string key,decimal price,DateTimeOffset at,string source,CancellationToken ct){if(price<=0)return;await using var c=db.CreateCommand("INSERT INTO paper.quotes VALUES(@k,@p,@t,@s) ON CONFLICT(contract_key) DO UPDATE SET price=excluded.price,observed_at=excluded.observed_at,source=excluded.source WHERE paper.quotes.observed_at<excluded.observed_at");c.Parameters.AddWithValue("k",key);c.Parameters.AddWithValue("p",price);c.Parameters.AddWithValue("t",at.ToUniversalTime());c.Parameters.AddWithValue("s",source);await c.ExecuteNonQueryAsync(ct);}
}
