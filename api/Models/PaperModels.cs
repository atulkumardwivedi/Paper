namespace PaperTrader;
public sealed class PaperOptions {
 public bool Enabled {get;set;}=true; public int PollSeconds {get;set;}=2;
 public bool AllowApiDownloads {get;set;} public int QuoteRefreshSeconds {get;set;}=30;
 public int MissingDataRetrySeconds {get;set;}=60; public int DailyApiLimit {get;set;}=5000;
 public int ReservedApiCalls {get;set;}=100; public int PerMinuteApiLimit {get;set;}=90;
 public int LotSize {get;set;}=65; public decimal SlippagePerFill {get;set;}
 public bool ExcludeExpiryDays {get;set;}=true; public int HedgeSearchPoints {get;set;}=1000;
}
public record Contract(decimal Strike,string Side) {
 public string Key(DateOnly expiry)=>$"NFO|OPTIONS|NIFTY|{expiry:yyyy-MM-dd}|{Strike:0.##}|{Side}";
}
public record Profile(string Id,string Strategy,int Lots,bool Margin,bool Protection,
 decimal Target,decimal Trigger,decimal NakedStop,decimal HedgedStop,int LotSize,decimal Slippage) {
 public int Quantity=>Lots*LotSize;
 public static List<Profile> All(PaperOptions o) {
  var r=new List<Profile>();
  foreach(var s in new[]{"Locked","MFShield"}) foreach(int lots in new[]{7,15})
   foreach(var h in new[]{"Margin","300","Both"}) {
    decimal scale=lots/15m;
    r.Add(new($"{s}-{lots}-{h}-v1",s,lots,h!="300",h!="Margin",
     (s=="Locked"?49000:100000)*scale,(s=="Locked"?8500:5000)*scale,
     (s=="Locked"?70000:25000)*scale,(s=="Locked"?10000:50000)*scale,o.LotSize,o.SlippagePerFill));
   }
  return r;
 }
}
public sealed class Leg { public Contract Contract {get;set;}=new(0,"CE"); public bool Buy {get;set;}
 public decimal Entry {get;set;} public DateTimeOffset EnteredAt {get;set;} public decimal? Exit {get;set;}
 public decimal? Mark {get;set;} public DateTimeOffset? MarkAt {get;set;} public string MarkSource {get;set;}=""; }
public sealed class Trade { public string Id {get;set;}=""; public DateTimeOffset SignalAt {get;set;}
 public DateTimeOffset EntryAt {get;set;} public DateTimeOffset? ExitAt {get;set;} public string Reason {get;set;}="";
 public bool Recovered {get;set;} public List<Leg> Legs {get;set;}=[]; public decimal Gross {get;set;}
 public decimal Costs {get;set;} public decimal Net {get;set;} public bool ProtectionActivated {get;set;} }
public sealed class Pending { public string Action {get;set;}=""; public int AtIndex {get;set;}
 public Contract? Short {get;set;} public Contract? Hedge {get;set;} public string Reason {get;set;}=""; public int SignalIndex {get;set;} }
public sealed class Portfolio { public Profile Profile {get;set;}=null!; public DateOnly Date {get;set;}
 public DateOnly Expiry {get;set;} public decimal MorningClose {get;set;} public List<Contract> Eight {get;set;}=[];
 public long Revision {get;set;} public int FilledExitIndex {get;set;}=-1; public int LastCloseIndex {get;set;}=-1; public int PreviousDirection {get;set;} public int Cooldown {get;set;}
 public Pending? Pending {get;set;} public List<Trade> Trades {get;set;}=[];
 public Trade? Active=>Trades.LastOrDefault(t=>t.ExitAt==null); public string Status {get;set;}="Waiting";
 public DateTimeOffset UpdatedAt {get;set;} public decimal Realized=>Trades.Where(t=>t.ExitAt!=null).Sum(t=>t.Net);
 public decimal OpenPnl=>Active?.Net??0; public bool DayComplete {get;set;}
}
