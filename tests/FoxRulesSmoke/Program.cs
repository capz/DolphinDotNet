using System;

internal static class Program
{
    private static int stage=1;
    private static void Check(bool value,string reason) { if(!value)throw new Exception(reason); }
    private static bool Near(float a,float b)=>a>b-.01f && a<b+.01f;
    private static FoxGame Fresh() { var game=new FoxGame();game.Start();return game; }
    private static void ClearThreats(FoxGame game)
    {
        for(int i=0;i<FoxGame.EnemyCount;i++)game.Enemies[i]=false;
        for(int i=0;i<FoxGame.BoltCount;i++)game.Bolts[i]=false;
    }
    private static void Advance(FoxGame game,int frames)
    { for(int i=0;i<frames;i++) { game.Update(.05f,0,0,false,false,false,0);ClearThreats(game); } }
    private static void Hit(FoxGame game)
    {
        game.Bolts[0]=true;game.BoltX[0]=game.PlayerX;game.BoltY[0]=game.PlayerY;
        game.BoltZ[0]=-.5f;game.BoltVx[0]=game.BoltVy[0]=0;
        game.Update(.001f,0,0,false,false,false,0);
    }
    private static int Main() { try { return Run(); }catch { return stage; } }
    private static int Run()
    {
        var game=new FoxGame();Check(game.State==FoxState.Title,"Title state.");
        Check(game.Update(.05f,1,1,true,true,false,1)==FoxEvent.None,"Title does not advance flight.");
        game.Start();Check(game.Shield==5 && game.Energy==1 && game.Distance==0,"Launch state.");
        game.Update(5,1,1,false,false,false,0);
        Check(Near(game.PlayerX,.4f) && Near(game.PlayerY,.3f) && Near(game.Distance,.9f),"Stalls cap at 50 ms.");
        for(int i=0;i<100;i++) { game.Update(.05f,1,1,false,false,false,0);ClearThreats(game); }
        Check(Near(game.PlayerX,5.2f) && Near(game.PlayerY,2.6f),"Right/top limits.");
        for(int i=0;i<100;i++) { game.Update(.05f,-1,-1,false,false,false,0);ClearThreats(game); }
        Check(Near(game.PlayerX,-5.2f) && Near(game.PlayerY,-2.4f),"Left/bottom limits.");
        bool rejected=false;try { game.Update(-1,0,0,false,false,false,0); }catch(ArgumentOutOfRangeException) { rejected=true; }
        Check(rejected,"Negative time rejected.");
        rejected=false;try { game.Update(float.NaN,0,0,false,false,false,0); }catch(ArgumentOutOfRangeException) { rejected=true; }
        Check(rejected,"NaN time rejected.");

        stage=2;game=Fresh();game.TogglePause();
        game.Update(.05f,1,1,true,true,false,1);
        Check(game.Distance==0 && game.PlayerX==0 && game.Energy==1 && game.RollRemaining==0,"Pause freezes movement, weapons and resources.");
        game.TogglePause();Check(game.State==FoxState.Playing,"Resume.");
        game.ReturnToTitle();Check(game.State==FoxState.Title && !game.BossActive,"Return to title.");

        stage=3;game=Fresh();
        game.Update(.05f,0,0,false,true,false,0);
        Check(game.Speed==30 && game.Energy<1 && game.Distance>1.4f,"Boost accelerates and drains energy.");
        for(int i=0;i<60;i++) { game.Update(.05f,0,0,false,true,false,0);ClearThreats(game); }
        Check(game.Speed==18,"Holding boost after exhaustion returns to cruise speed.");
        Advance(game,90);Check(game.Energy>.99f,"Releasing boost recharges.");
        game.Update(.05f,0,0,false,true,true,0);Check(game.Speed==10,"Brake takes priority over boost.");
        game.Update(.05f,0,0,false,true,false,0);Check(game.Speed==30,"Boost is available again after release.");

        stage=4;game=Fresh();
        var events=game.Update(.001f,0,0,false,false,false,-1);
        Check((events&FoxEvent.Roll)!=0 && game.RollRemaining>0 && game.RollDirection==-1,"Left barrel roll starts.");
        Hit(game);Check(game.Shield==5,"Barrel roll protects against a bolt.");
        events=game.Update(.001f,0,0,false,false,false,1);
        Check((events&FoxEvent.Roll)==0 && game.RollDirection==-1,"Cooldown prevents spamming rolls.");
        Advance(game,40);events=game.Update(.001f,0,0,false,false,false,1);
        Check((events&FoxEvent.Roll)!=0 && game.RollDirection==1,"Right roll available after cooldown.");

        stage=5;game=Fresh();events=game.Update(.001f,0,0,true,false,false,0);
        Check((events&FoxEvent.Shot)!=0 && game.Shots[0] && game.ShotZ[0]< -2,"Laser fires forward.");
        events=game.Update(.001f,0,0,true,false,false,0);
        Check((events&FoxEvent.Shot)==0 && !game.Shots[1],"Fire rate is bounded.");
        game.Enemies[0]=true;game.EnemyX[0]=0;game.EnemyY[0]=0;game.EnemyZ[0]=-3;
        game.EnemyDrift[0]=0;game.EnemyFire[0]=10;
        game.Shots[0]=true;game.ShotX[0]=0;game.ShotY[0]=0;game.ShotZ[0]=-2.5f;
        events=game.Update(.001f,0,0,false,false,false,0);
        Check((events&FoxEvent.EnemyHit)!=0 && !game.Enemies[0] && !game.Shots[0] && game.Score==25,"Laser destroys one fighter and scores.");
        game.Shots[0]=true;game.ShotZ[0]=-141;game.Update(.001f,0,0,false,false,false,0);
        Check(!game.Shots[0],"Distant lasers expire.");
        game.ReturnToTitle();game.Start();Check(game.Score==0 && game.HighScore==25,"Session best survives restart.");

        stage=6;game=Fresh();Hit(game);
        Check(game.Shield==4 && game.Invulnerable>0,"Damage consumes shield and starts grace period.");
        Hit(game);Check(game.Shield==4,"Repeated hit during grace does not drain shield.");
        game.Rings[0]=true;game.RingX[0]=0;game.RingY[0]=0;game.RingZ[0]=-.5f;
        events=game.Update(.001f,0,0,false,false,false,0);
        Check((events&FoxEvent.Ring)!=0 && game.Shield==5 && game.Energy==1 && game.Score==50 && !game.Rings[0],"Gold ring restores shield/energy and scores.");
        game.Rings[0]=true;game.RingZ[0]=6;game.Update(.001f,0,0,false,false,false,0);
        Check(!game.Rings[0],"Missed rings expire.");
        for(int i=4;i>=0;i--) { Advance(game,22);Hit(game);Check(game.Shield==i,"Later impacts consume exactly one shield."); }
        Check(game.State==FoxState.GameOver,"Zero shield ends mission.");
        float distance=game.Distance;game.Update(.05f,1,1,true,true,false,1);
        Check(game.Distance==distance,"Game over freezes simulation.");

        stage=7;game=Fresh();game.Enemies[0]=true;game.EnemyX[0]=game.EnemyY[0]=0;
        game.EnemyZ[0]=-.5f;game.EnemyFire[0]=10;
        game.Update(.001f,0,0,false,false,false,0);
        Check(game.Shield==4 && !game.Enemies[0],"Fighter collision damages the ship.");
        game=Fresh();Advance(game,1100);
        Check(game.BossActive && game.BossHealth==20 && game.State==FoxState.Playing,"Route reaches boss without external data.");
        int before=game.Score;
        for(int i=0;i<20;i++)
        {
            game.Shots[0]=true;game.ShotX[0]=game.BossX;game.ShotY[0]=game.BossY;game.ShotZ[0]=game.BossZ+.05f;
            events=game.Update(.001f,0,0,false,false,false,0);
            Check((events&FoxEvent.EnemyHit)!=0 && game.BossHealth==19-i,"Boss takes one damage per laser.");
        }
        Check(game.State==FoxState.Victory && game.Score==before+500 && (events&FoxEvent.Victory)!=0,"Boss defeat wins and awards points.");
        game.Start();Check(!game.BossActive && game.Shield==5 && game.Distance==0,"New mission clears boss and repairs ship.");

        stage=8;var a=Fresh();var b=Fresh();
        for(int frame=0;frame<100;frame++)
        {
            a.Update(.02f,0,0,false,false,false,0);b.Update(.02f,0,0,false,false,false,0);
            for(int i=0;i<FoxGame.EnemyCount;i++)
                Check(a.Enemies[i]==b.Enemies[i] && a.EnemyX[i]==b.EnemyX[i] && a.EnemyZ[i]==b.EnemyZ[i],"Formations are deterministic.");
        }
        bool spawned=false;for(int i=0;i<FoxGame.EnemyCount;i++)spawned|=a.Enemies[i];
        Check(spawned,"Fighters spawn during flight.");
        var pcm=FoxWaveforms.Pulse(1400,380,65);
        Check(pcm.Length==1433 && pcm[pcm.Length-1]==128,"Sound length and fade.");
        return 0;
    }
}
