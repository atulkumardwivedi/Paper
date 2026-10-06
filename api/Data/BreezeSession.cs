using Npgsql;
namespace NiftyPremiumTrader.Api.Data;
// Minimal session reader only; this application has no order client.
public sealed class CandleRepository(NpgsqlDataSource db) {
 public async Task<string?> GetIciciSessionAsync(CancellationToken ct){
  try {await using var c=db.CreateCommand("SELECT icicisession FROM sessionmaster LIMIT 1");return (await c.ExecuteScalarAsync(ct))?.ToString();}
  catch(PostgresException e) when(e.SqlState=="42P01"){return null;}
 }
}
