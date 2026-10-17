using System;

internal static class Program
{
    private static int stage=1;
    private static void Check(bool value,string message) { if(!value)throw new Exception(message); }
    private static bool Near(float a,float b)=>a>b-0.01f && a<b+0.01f;
    private static GreenMenGame Fresh()
    {
        var game=new GreenMenGame();game.Start();game.Update(0.001f,0,false);return game;
    }
    private static void Advance(GreenMenGame game,int frames)
    { for(int i=0;i<frames;i++)game.Update(1f/120,0,false); }
    private static void ClearShields(GreenMenGame game)
    { for(int i=0;i<game.Shields.Length;i++)game.Shields[i]=false; }
    private static void ClearAliensExcept(GreenMenGame game,int keep)
    { for(int i=0;i<game.Aliens.Length;i++)game.Aliens[i]=i==keep; }
    private static void HitPlayer(GreenMenGame game)
    {
        game.Bombs[0]=true;game.BombX[0]=game.PlayerX;game.BombY[0]=GreenMenGame.PlayerY-6;
        var events=game.Update(0.001f,0,false);
        Check((events&GreenMenEvent.PlayerHit)!=0,"Player collision reports damage.");
    }
    private static int Main()
    {
        try { return Run(); }
        catch { return stage; }
    }
    private static int Run()
    {
        var game=new GreenMenGame();
        Check(game.State==GreenMenState.Title,"Starts on title screen.");
        Check(game.Update(0.05f,1,true)==GreenMenEvent.None,"Title cannot simulate or fire.");
        game.Start();
        Check(game.Lives==3 && game.Wave==1 && game.Score==0 && game.Remaining==32,"New game state.");
        Check(!game.Shields[0] && game.Shields[1] && game.Shields[6] && !game.Shields[20],"Bunker shape and arch.");
        float x=game.PlayerX;
        game.Update(1f,1,false);
        Check(Near(game.PlayerX,x+13.5f),"Long stalls are clamped to 50 ms.");
        for(int i=0;i<80;i++)game.Update(0.05f,1,false);
        Check(Near(game.PlayerX,610),"Ship clamps at right edge.");
        for(int i=0;i<80;i++)game.Update(0.05f,-1,false);
        Check(Near(game.PlayerX,30),"Ship clamps at left edge.");
        game=Fresh();game.TogglePause();
        x=game.PlayerX;float formation=game.FormationX;
        Check(game.Update(0.05f,1,true)==GreenMenEvent.None && game.PlayerX==x && game.FormationX==formation,"Pause freezes simulation.");
        game.TogglePause();Check(game.State==GreenMenState.Playing,"Resume returns to play.");
        bool rejected=false;
        try { game.Update(-1,0,false); }catch(ArgumentOutOfRangeException) { rejected=true; }
        Check(rejected,"Reject negative time.");
        rejected=false;
        try { game.Update(float.NaN,0,false); }catch(ArgumentOutOfRangeException) { rejected=true; }
        Check(rejected,"Reject NaN time.");

        stage=2;
        // One player shot at a time; holding fire does not overwrite a live projectile.
        game=Fresh();ClearShields(game);
        Check((game.Update(0.001f,0,true)&GreenMenEvent.Shot)!=0 && game.ShotActive,"Fire creates a shot.");
        float shotY=game.ShotY;
        Check((game.Update(0.001f,0,true)&GreenMenEvent.Shot)==0 && game.ShotY<shotY,"Holding does not replace a shot.");
        game.ShotY=45;game.Update(0.004f,0,false);Check(!game.ShotActive,"Shots expire above playfield.");

        stage=3;
        // Both sides damage bunker cells and consume the projectile.
        game=Fresh();int cell=6;
        game.ShotActive=true;game.ShotX=GreenMenGame.ShieldX(cell)+3;game.ShotY=GreenMenGame.ShieldCellY(cell)+2;
        game.Update(0.001f,0,false);
        Check(!game.Shields[cell] && !game.ShotActive && game.Score==0,"Player shot damages a shield, not score.");
        stage=4;
        cell=7;game.Bombs[0]=true;game.BombX[0]=GreenMenGame.ShieldX(cell)+3;game.BombY[0]=GreenMenGame.ShieldCellY(cell)+1;
        game.Update(0.001f,0,false);Check(!game.Shields[cell] && !game.Bombs[0],"Enemy shot damages a shield.");

        stage=5;
        // Alien score tiers and single-hit projectile arbitration.
        game=Fresh();ClearShields(game);int target=24;
        game.ShotActive=true;game.ShotX=game.AlienX(target)+12;game.ShotY=game.AlienY(target)+10;
        var hit=game.Update(0.001f,0,false);
        Check((hit&GreenMenEvent.AlienHit)!=0 && !game.Aliens[target] && game.Remaining==31 && game.Score==10,"Bottom alien scores ten.");
        target=0;game.ShotActive=true;game.ShotX=game.AlienX(target)+12;game.ShotY=game.AlienY(target)+10;
        game.Update(0.001f,0,false);Check(game.Score==50 && game.HighScore==50,"Top alien scores forty; best tracks score.");
        game.ReturnToTitle();game.Start();Check(game.Score==0 && game.HighScore==50,"Best survives a new game within the session.");

        stage=6;
        // A final kill clears shots, freezes the wave timer when paused, then rebuilds the formation.
        game=Fresh();ClearShields(game);ClearAliensExcept(game,24);
        game.ShotActive=true;game.ShotX=game.AlienX(24)+12;game.ShotY=game.AlienY(24)+10;
        game.Bombs[0]=true;
        game.Update(0.001f,0,false);
        Check(game.State==GreenMenState.WaveClear && game.Remaining==0 && !game.Bombs[0],"Last kill enters wave clear and clears enemy shots.");
        game.TogglePause();Advance(game,200);game.TogglePause();
        Check(game.State==GreenMenState.WaveClear && game.Wave==1,"Pausing also freezes wave transitions.");
        Advance(game,140);
        Check(game.Wave==2 && game.State==GreenMenState.Playing && game.Remaining==32 && game.Lives==3,"Next wave keeps lives and restocks aliens.");
        Check(game.Shields[6],"New wave repairs bunkers.");

        stage=7;
        // Marching uses living bounds, reverses/descends, and speeds up with fewer aliens.
        game=Fresh();ClearAliensExcept(game,7);game.FormationX=260;
        float oldY=game.FormationY;
        Advance(game,140);
        Check(game.FormationY>oldY && game.FormationX<260,"Right boundary causes descent and leftward reversal.");
        Check(game.Beat>4,"Last alien marches faster than a full formation.");
        game=Fresh();ClearAliensExcept(game,0);game.FormationY=400;
        Advance(game,80);
        Check(game.State==GreenMenState.GameOver && game.Lives==0,"Invasion ends the game.");

        stage=8;
        // Three lives, invulnerability, no double damage, and restart after defeat.
        game=Fresh();ClearShields(game);
        HitPlayer(game);Check(game.Lives==2 && game.Invulnerable>1,"Damage grants a grace period.");
        game.Bombs[0]=true;game.BombX[0]=game.PlayerX;game.BombY[0]=GreenMenGame.PlayerY-6;
        game.Update(0.001f,0,false);Check(game.Lives==2,"Grace period prevents double damage.");
        for(int life=1;life>=0;life--)
        {
            Advance(game,190);HitPlayer(game);Check(game.Lives==life,"Each later hit loses exactly one life.");
        }
        Check(game.State==GreenMenState.GameOver,"Three hits reach game over.");
        x=game.PlayerX;game.Update(0.05f,1,true);Check(game.PlayerX==x && !game.ShotActive,"Game over cannot move or shoot.");
        game.Start();Check(game.State==GreenMenState.Playing && game.Lives==3 && game.Invulnerable==0,"Restart restores lives and clears grace period.");

        stage=9;
        // Lowest living row alone fires. Four slots remain bounded, and identical inputs are deterministic.
        var a=Fresh();var b=Fresh();
        for(int i=0;i<8;i++) { a.Aliens[24+i]=false;b.Aliens[24+i]=false; }
        for(int i=0;i<110;i++)
        {
            a.Update(1f/120,0,false);b.Update(1f/120,0,false);
            for(int shot=0;shot<GreenMenGame.BombCount;shot++)
            {
                Check(a.Bombs[shot]==b.Bombs[shot] && a.BombX[shot]==b.BombX[shot] && a.BombY[shot]==b.BombY[shot],"Enemy fire is deterministic.");
                if(a.Bombs[shot])Check(a.BombY[shot]>=a.AlienY(16)+GreenMenGame.AlienHeight,"Bomb originates from the lowest living row.");
            }
        }
        bool bomb=false;for(int i=0;i<4;i++)bomb|=a.Bombs[i];Check(bomb,"Enemy firing occurs during play.");
        stage=10;
        byte[] sound=GreenMenWaveforms.Pulse(1400,380,65);
        Check(sound.Length==1433 && sound[sound.Length-1]==128,"PCM effect has deterministic length and decays to silence.");
        bool audible=false;for(int i=0;i<sound.Length;i++)if(sound[i]!=128)audible=true;
        Check(audible,"Synthesized shot is audible.");
        return 0;
    }
}
