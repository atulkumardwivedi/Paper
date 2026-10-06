using NiftyPremiumTrader.Api.Models;
using NiftyPremiumTrader.Api.Services;
namespace PaperTrader;
public sealed class PaperEngine(Store store,MarketData market,PaperOptions options) {
 public static DateTimeOffset Time(DateOnly d,int i)=>MarketClock.ToUtc(d,new TimeOnly(9,15)).AddMinutes(i*5);
 public static List<Contract> Select(decimal close){
  decimal a=Math.Round(close/100m,MidpointRounding.ToEven)*100;
  int[] ce=a>close?[-100,0,100,200]:[300,200,100,0];int[] pe=a>close?[0,-100,-200,-300]:[0,-100,-200,100];
  return ce.Order().Select(x=>new Contract(a+x,"CE")).Concat(pe.OrderDescending().Select(x=>new Contract(a+x,"PE"))).ToList();
 }
 public static void Mark(Portfolio s,Trade t){
  t.Gross=t.Legs.Sum(l=>((l.Exit??l.Mark??l.Entry)-l.Entry)*(l.Buy?1:-1)*s.Profile.Quantity);
  t.Costs=t.Legs.Count*225m*s.Profile.Lots/10;t.Net=t.Gross-t.Costs;
 }
 static OptionCandle? Find(List<OptionCandle> all,DateOnly expiry,Contract k,DateTimeOffset time)=>all.FirstOrDefault(c=>c.ExpiryDate==expiry&&c.Strike==k.Strike&&c.OptionType==k.Side&&c.CandleTime==time);
 static decimal Median(List<decimal> x){x.Sort();return (x[2]+x[3])/2;}
 public static int Warning(List<Contract> eight,List<OptionCandle> all,DateOnly date,DateOnly expiry,int i){
  if(i<7)return 0;var ranges=new List<(decimal C,decimal P)>();decimal[] sums=new decimal[8];decimal[] prior=new decimal[8];
  for(int n=0;n<=i;n++){
   decimal[] mf=new decimal[8];
   for(int k=0;k<8;k++){
    var c=Find(all,expiry,eight[k],Time(date,n));if(c==null||!c.IsComplete)return 0;
    decimal typical=(c.High+c.Low+c.Close)/3;long volume=n==0&&c.Volume==0?1:c.Volume;
    sums[k]+=typical*volume*(n>0&&typical<prior[k]?-1:1);prior[k]=typical;mf[k]=Math.Round(sums[k]/10000000m,2,MidpointRounding.ToEven);
   }
   ranges.Add((mf.Take(4).Max()-mf.Take(4).Min(),mf.Skip(4).Max()-mf.Skip(4).Min()));
  }
  var cb=Median(ranges.Skip(i-6).Take(6).Select(x=>x.C).ToList());var pb=Median(ranges.Skip(i-6).Take(6).Select(x=>x.P).ToList());var r=ranges[i];
  return cb>0&&pb>0&&r.C<=.7m*cb&&r.P>=1.8m*pb?1:cb>0&&pb>0&&r.P<=.7m*pb&&r.C>=1.8m*cb?-1:0;
 }
 public async Task Advance(Portfolio s,CancellationToken ct){
  if(s.DayComplete)return;
  var now=DateTimeOffset.UtcNow;
  if(now<Time(s.Date,1)){s.Status="Waiting for first candle close at 09:20 IST";return;}
  if(s.Eight.Count==0){
   if(!await market.Ensure(s.Date,s.Expiry,null,Time(s.Date,0),true,ct)){s.Status="Waiting: first cash candle missing";return;}
   var cash=(await store.Candles(s.Date,ct)).First(c=>c.ProductType=="cash"&&c.CandleTime==Time(s.Date,0)&&c.IsComplete);
   s.MorningClose=cash.Close;s.Eight=Select(cash.Close);await store.Save(s,ct);
  }
  for(int i=s.LastCloseIndex+1;i<=72&&Time(s.Date,i)<=now;i++){
   var time=Time(s.Date,i);bool complete=time.AddMinutes(5)<=now;
   var all=await store.Candles(s.Date,ct);
   if(s.Pending is {} pending&&i>=pending.AtIndex){
    var legs=pending.Action=="ENTRY"?new[]{pending.Short,pending.Hedge}.OfType<Contract>().ToList():pending.Action=="HEDGE"?new List<Contract>{pending.Hedge!}:s.Active!.Legs.Select(l=>l.Contract).ToList();
    bool ready=true;foreach(var k in legs)ready=await market.Ensure(s.Date,s.Expiry,k,Time(s.Date,pending.AtIndex),false,ct)&&ready;
    if(!ready){
     s.Status=$"Waiting for {pending.Action} fill candle {Time(s.Date,pending.AtIndex):HH:mm} UTC";
     if(pending.Action=="HEDGE"){s.Pending=null;}else {await store.Save(s,ct);return;}
    } else {
     all=await store.Candles(s.Date,ct);decimal Fill(Contract k,bool buy)=>Math.Max(.05m,Find(all,s.Expiry,k,Time(s.Date,pending.AtIndex))!.Open+(buy?s.Profile.Slippage:-s.Profile.Slippage));
     var at=Time(s.Date,pending.AtIndex);
     if(pending.Action=="ENTRY"){
      var t=new Trade{Id=$"{s.Profile.Id}/{s.Date}/{pending.SignalIndex}",SignalAt=Time(s.Date,pending.SignalIndex).AddMinutes(5),EntryAt=at,Recovered=now>at.AddMinutes(5)};
      t.Legs.Add(new(){Contract=pending.Short!,Entry=Fill(pending.Short!,false),EnteredAt=at});
      if(pending.Hedge!=null)t.Legs.Add(new(){Contract=pending.Hedge,Buy=true,Entry=Fill(pending.Hedge,true),EnteredAt=at});
      if(s.Trades.Any(x=>x.Id==t.Id))throw new InvalidOperationException("Duplicate trade id rejected");s.Trades.Add(t);Mark(s,t);
     } else if(pending.Action=="HEDGE"){
      var t=s.Active!;t.Recovered|=now>at.AddMinutes(5);t.Legs.Add(new(){Contract=pending.Hedge!,Buy=true,Entry=Fill(pending.Hedge!,true),EnteredAt=at});t.ProtectionActivated=true;
     } else {
      var t=s.Active!;t.Recovered|=now>at.AddMinutes(5);foreach(var leg in t.Legs)leg.Exit=Fill(leg.Contract,!leg.Buy);t.ExitAt=at;t.Reason=pending.Reason;Mark(s,t);s.Cooldown=2;s.FilledExitIndex=i;
     }
     s.Pending=null;await store.Save(s,ct);
     if(pending.Action=="EXIT"){
      if(i>=72){s.DayComplete=true;s.LastCloseIndex=72;s.Status="Day complete";await store.Save(s,ct);return;}
      // Match archived replay: exit bar consumes the signal but no re-entry.
      if(complete){s.PreviousDirection=Direction(s,all,time);s.LastCloseIndex=i;await store.Save(s,ct);continue;}
      return;
     }
    }
   }
   if(i>=72){
    if(s.Active!=null){s.Pending=new(){Action="EXIT",AtIndex=72,Reason="EOD",SignalIndex=71};await store.Save(s,ct);return;}
    s.DayComplete=true;s.LastCloseIndex=72;s.Status="Day complete";await store.Save(s,ct);return;
   }
   if(!complete)return;
   if(s.FilledExitIndex==i){s.PreviousDirection=Direction(s,all,time);s.LastCloseIndex=i;await store.Save(s,ct);continue;}
   bool exists=true;foreach(var k in s.Eight)exists=await market.Ensure(s.Date,s.Expiry,k,time,true,ct)&&exists;
   exists=await market.Ensure(s.Date,s.Expiry,null,time,true,ct)&&exists;
   if(!exists){
    s.Status=$"Missing signal data at {MarketClock.ToIndia(time):HH:mm}; no entry";
    if(s.Active==null){s.PreviousDirection=0;s.LastCloseIndex=i;await store.Save(s,ct);continue;}
    // An existing trade still receives price-based stops even without all signal pairs.
   }
   all=await store.Candles(s.Date,ct);int dir=exists?Direction(s,all,time):0;bool fresh=dir!=s.PreviousDirection;s.PreviousDirection=dir;
   if(s.Active is {} trade){
    bool have=true;foreach(var l in trade.Legs)have=await market.Ensure(s.Date,s.Expiry,l.Contract,time,true,ct)&&have;
    if(!have){s.Status="Open position: waiting for missing leg data";await store.Save(s,ct);return;}
    all=await store.Candles(s.Date,ct);foreach(var l in trade.Legs){l.Mark=Find(all,s.Expiry,l.Contract,time)!.Close;l.MarkAt=time.AddMinutes(5);l.MarkSource="5-minute database close";}
    Mark(s,trade);bool hedged=trade.Legs.Count>1;decimal shortPnl=(trade.Legs[0].Entry-trade.Legs[0].Mark!.Value)*s.Profile.Quantity;
    string reason=trade.Gross>=s.Profile.Target?"TARGET":trade.Gross<=-(hedged?s.Profile.HedgedStop:s.Profile.NakedStop)?"STOP":"";
    int entryIndex=(int)(trade.EntryAt-Time(s.Date,0)).TotalMinutes/5;
    if(reason==""&&s.Profile.Strategy=="MFShield"&&i-entryIndex>=1){int bias=trade.Legs[0].Contract.Side=="PE"?1:-1;if(Warning(s.Eight,all,s.Date,s.Expiry,i)*bias<0)reason="MF_SQUEEZE_EXIT";}
    if(reason!="")s.Pending=new(){Action="EXIT",AtIndex=i+1,SignalIndex=i,Reason=reason};
    else if(s.Profile.Protection&&!trade.ProtectionActivated&&shortPnl<=-s.Profile.Trigger){
     var k=trade.Legs[0].Contract;var hedge=new Contract(k.Strike+(k.Side=="CE"?300:-300),k.Side);
     if(trade.Legs.Any(l=>l.Buy&&l.Contract==hedge))trade.ProtectionActivated=true;
     else if(await market.Ensure(s.Date,s.Expiry,hedge,time,true,ct))s.Pending=new(){Action="HEDGE",AtIndex=i+1,SignalIndex=i,Hedge=hedge};
     else s.Status="Protection hedge unavailable; existing stop remains active";
    }
   } else if(s.Cooldown>0)s.Cooldown--;
   else if(i>=3&&i<=58&&fresh&&dir!=0){
    var spot=all.First(c=>c.ProductType=="cash"&&c.CandleTime==time).Close;
    var candidates=s.Eight.Where(k=>k.Side==(dir==1?"PE":"CE")).Select(k=>(k,c:Find(all,s.Expiry,k,time)!)).Select(x=>(x.k,delta:x.c.Delta??OptionDeltaCalculator.Estimate(x.c,spot))).Where(x=>x.delta.HasValue&&Math.Abs(x.delta.Value)>0&&Math.Abs(x.delta.Value)<=1).OrderBy(x=>Math.Abs(Math.Abs(x.delta!.Value)-.5m)).ToList();
    if(candidates.Count>0){
     var k=candidates[0].k;Contract? hedge=null;
     if(s.Profile.Margin){
      decimal target=Find(all,s.Expiry,k,time)!.Close*.1m;
      // Only fixed100grid farther OTM strikes; source quotes are signal-time CLOSEs.
      var pool=new List<Contract>();for(int gap=100;gap<=options.HedgeSearchPoints;gap+=100){var h=new Contract(k.Strike+(k.Side=="CE"?gap:-gap),k.Side);if(k.Side=="CE"?h.Strike<=spot:h.Strike>=spot)continue;if(await market.Ensure(s.Date,s.Expiry,h,time,true,ct))pool.Add(h);}
      all=await store.Candles(s.Date,ct);hedge=pool.OrderBy(h=>Math.Abs(Find(all,s.Expiry,h,time)!.Close-target)).ThenBy(h=>Math.Abs(h.Strike-k.Strike)).FirstOrDefault();
     }
     if(!s.Profile.Margin||hedge!=null)s.Pending=new(){Action="ENTRY",AtIndex=i+1,SignalIndex=i,Short=k,Hedge=hedge};else s.Status="Entry skipped: margin hedge unavailable";
    }
   }
   s.LastCloseIndex=i;if(!s.Status.Contains("unavailable"))s.Status=s.Active==null?"Watching completed candles":"Open paper position";await store.Save(s,ct);
  }
 }
 public static int Direction(Portfolio s,List<OptionCandle> all,DateTimeOffset time){
  int bull=0,bear=0;for(int k=0;k<4;k++){
   var c=Find(all,s.Expiry,s.Eight[k],time);var p=Find(all,s.Expiry,s.Eight[k+4],time);if(c==null||p==null||c.Open<=0||p.Open<=0)return 0;
   var cb=(c.Close-c.Open)/c.Open;var pb=(p.Close-p.Open)/p.Open;if(cb>=.01m&&pb<=-.01m)bull++;if(cb<=-.01m&&pb>=.01m)bear++;
  }return bull>=2&&bull>bear?1:bear>=2&&bear>bull?-1:0;
 }
}
