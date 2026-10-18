using System;

internal enum FoxState { Title, Playing, Paused, Victory, GameOver }
[Flags]
internal enum FoxEvent { None=0, Shot=1, EnemyHit=2, Damage=4, Ring=8, Roll=16, Victory=32 }

/// <summary>Deterministic rail-shooter rules. Ship moves in XY; distant objects approach along +Z.</summary>
internal sealed class FoxGame
{
    internal const int EnemyCount=8, ShotCount=16, BoltCount=12, RingCount=4, MaximumShield=5;
    internal const float BossDistance=900, RollDuration=0.65f;
    internal readonly bool[] Enemies=new bool[EnemyCount], Shots=new bool[ShotCount], Bolts=new bool[BoltCount], Rings=new bool[RingCount];
    internal readonly float[] EnemyX=new float[EnemyCount],EnemyY=new float[EnemyCount],EnemyZ=new float[EnemyCount],EnemyDrift=new float[EnemyCount],EnemyFire=new float[EnemyCount];
    internal readonly float[] ShotX=new float[ShotCount],ShotY=new float[ShotCount],ShotZ=new float[ShotCount];
    internal readonly float[] BoltX=new float[BoltCount],BoltY=new float[BoltCount],BoltZ=new float[BoltCount],BoltVx=new float[BoltCount],BoltVy=new float[BoltCount];
    internal readonly float[] RingX=new float[RingCount],RingY=new float[RingCount],RingZ=new float[RingCount];
    internal FoxState State { get; private set; }
    internal float PlayerX { get; private set; }
    internal float PlayerY { get; private set; }
    internal float Distance { get; private set; }
    internal float SceneTravel { get; private set; }
    internal float Speed { get; private set; }
    internal float Energy { get; private set; }
    internal float Invulnerable { get; private set; }
    internal float RollRemaining { get; private set; }
    internal float RollCooldown { get; private set; }
    internal int RollDirection { get; private set; }
    internal float Bank { get; private set; }
    internal int Shield { get; private set; }
    internal int Score { get; private set; }
    internal int HighScore { get; private set; }
    internal void LoadBest(int score){if(score>HighScore)HighScore=score;}
    internal bool BossActive { get; private set; }
    internal int BossHealth { get; private set; }
    internal float BossX,BossY,BossZ;
    private float fireTime,spawnTime,nextRing,bossFire,bossDirection;
    private int formation;
    private bool boostExhausted;

    internal FoxGame() { State=FoxState.Title; }
    internal void Start()
    {
        ClearObjects();PlayerX=PlayerY=Distance=Bank=SceneTravel=0;boostExhausted=false;
        Score=0;Shield=MaximumShield;Energy=1;Speed=18;
        RollRemaining=RollCooldown=Invulnerable=0;RollDirection=1;
        BossActive=false;BossHealth=0;formation=0;
        fireTime=0;spawnTime=0.4f;nextRing=150;bossFire=1;bossDirection=1;
        State=FoxState.Playing;
    }
    internal void TogglePause()
    {
        if(State==FoxState.Playing)State=FoxState.Paused;
        else if(State==FoxState.Paused)State=FoxState.Playing;
    }
    internal void ReturnToTitle() { State=FoxState.Title;ClearObjects();BossActive=false; }
    private void ClearObjects()
    {
        for(int i=0;i<EnemyCount;i++)Enemies[i]=false;
        for(int i=0;i<ShotCount;i++)Shots[i]=false;
        for(int i=0;i<BoltCount;i++)Bolts[i]=false;
        for(int i=0;i<RingCount;i++)Rings[i]=false;
    }
    private static float Clamp(float x,float low,float high)=>Math.Clamp(x,low,high);
    private static float Abs(float v)=>MathF.Abs(v);
    private static bool Close(float x,float y,float a,float b,float radius)
    { float dx=x-a,dy=y-b;return dx*dx+dy*dy<radius*radius; }
    private void AddScore(int points) { Score+=points;if(Score>HighScore)HighScore=Score; }
    private FoxEvent Damage()
    {
        if(Invulnerable>0)return FoxEvent.None;
        Shield--;Invulnerable=1.0f;
        if(Shield<=0) { Shield=0;State=FoxState.GameOver;ClearObjects(); }
        return FoxEvent.Damage;
    }
    private void SpawnFormation()
    {
        for(int lane=0;lane<3;lane++)
            for(int i=0;i<EnemyCount;i++)if(!Enemies[i])
            {
                Enemies[i]=true;EnemyX[i]=(lane-1)*3.5f;
                EnemyY[i]=((formation+lane)%3-1)*1.5f;EnemyZ[i]=-105-lane*6;
                EnemyDrift[i]=(lane-1)*0.35f;EnemyFire[i]=1.8f+lane*0.25f;break;
            }
        formation++;
    }
    private void FireBolt(float x,float y,float z)
    {
        for(int i=0;i<BoltCount;i++)if(!Bolts[i])
        {
            Bolts[i]=true;BoltX[i]=x;BoltY[i]=y;BoltZ[i]=z;
            float travel=-z/42;if(travel<0.2f)travel=0.2f;
            BoltVx[i]=(PlayerX-x)/travel;BoltVy[i]=(PlayerY-y)/travel;return;
        }
    }
    private bool FireShot()
    {
        for(int i=0;i<ShotCount;i++)if(!Shots[i])
        { Shots[i]=true;ShotX[i]=PlayerX;ShotY[i]=PlayerY;ShotZ[i]=-2;return true; }
        return false;
    }
    internal FoxEvent Update(float seconds,float horizontal,float vertical,bool fire,bool boost,bool brake,int roll)
    {
        if(!(seconds>=0))throw new ArgumentOutOfRangeException("seconds");
        if(State!=FoxState.Playing)return FoxEvent.None;
        seconds=Clamp(seconds,0,0.05f);horizontal=Clamp(horizontal,-1,1);vertical=Clamp(vertical,-1,1);
        if(!boost)boostExhausted=false;
        FoxEvent events=FoxEvent.None;
        if(roll!=0 && RollCooldown<=0)
        {
            RollRemaining=RollDuration;RollCooldown=1.7f;RollDirection=roll<0?-1:1;
            if(Invulnerable<RollDuration)Invulnerable=RollDuration;events|=FoxEvent.Roll;
        }
        while(seconds>0 && State==FoxState.Playing)
        {
            float dt=seconds>1f/240?1f/240:seconds;seconds-=dt;
            PlayerX=Clamp(PlayerX+horizontal*8*dt,-5.2f,5.2f);
            PlayerY=Clamp(PlayerY+vertical*6*dt,-2.4f,2.6f);Bank=-horizontal*24;
            Invulnerable=Clamp(Invulnerable-dt,0,1);
            RollRemaining=Clamp(RollRemaining-dt,0,RollDuration);RollCooldown=Clamp(RollCooldown-dt,0,2);
            if(Energy<=0)boostExhausted=true;
            Speed=brake?10:boost && !boostExhausted && Energy>0?30:18;
            Energy=Clamp(Energy+(Speed==30?-0.48f:0.25f)*dt,0,1);
            SceneTravel+=Speed*dt;if(SceneTravel>=12)SceneTravel-=12;
            if(!BossActive)Distance+=Speed*dt;
            spawnTime-=dt;
            if(!BossActive && Distance<BossDistance && spawnTime<=0)
            { SpawnFormation();spawnTime=2.7f; }
            if(!BossActive && Distance>=nextRing && Distance<BossDistance)
            {
                for(int i=0;i<RingCount;i++)if(!Rings[i])
                { Rings[i]=true;RingX[i]=(formation%3-1)*2;RingY[i]=0;RingZ[i]=-85;break; }
                nextRing+=180;
            }
            if(!BossActive && Distance>=BossDistance)
            {
                ClearObjects();BossActive=true;BossHealth=20;BossX=0;BossY=1;BossZ=-48;
            }
            if(BossActive)
            {
                BossX+=bossDirection*1.2f*dt;
                if(BossX>4) { BossX=4;bossDirection=-1; }
                else if(BossX< -4) { BossX=-4;bossDirection=1; }
                bossFire-=dt;
                if(bossFire<=0) { FireBolt(BossX-1.2f,BossY,BossZ);FireBolt(BossX+1.2f,BossY,BossZ);bossFire=0.9f; }
            }
            fireTime-=dt;
            if(fire && fireTime<=0 && FireShot()) { fireTime=0.16f;events|=FoxEvent.Shot; }
            for(int i=0;i<EnemyCount;i++)if(Enemies[i])
            {
                EnemyZ[i]+=(Speed+5)*dt;EnemyX[i]+=EnemyDrift[i]*dt;
                if(EnemyX[i]< -5 || EnemyX[i]>5)EnemyDrift[i]=-EnemyDrift[i];
                EnemyFire[i]-=dt;
                if(EnemyFire[i]<=0 && EnemyZ[i]< -8)
                { FireBolt(EnemyX[i],EnemyY[i],EnemyZ[i]);EnemyFire[i]=2.3f; }
                if(Abs(EnemyZ[i])<1.6f && Close(PlayerX,PlayerY,EnemyX[i],EnemyY[i],1.1f))
                { Enemies[i]=false;events|=Damage(); }
                else if(EnemyZ[i]>4)Enemies[i]=false;
            }
            if(State!=FoxState.Playing)break;
            for(int i=0;i<ShotCount;i++)if(Shots[i])
            {
                ShotZ[i]-=100*dt;
                if(ShotZ[i]< -140) { Shots[i]=false;continue; }
                if(BossActive && Abs(ShotZ[i]-BossZ)<2 &&
                   Abs(ShotX[i]-BossX)<2.8f && Abs(ShotY[i]-BossY)<1.4f)
                {
                    Shots[i]=false;BossHealth--;events|=FoxEvent.EnemyHit;
                    if(BossHealth<=0)
                    { BossHealth=0;AddScore(500);State=FoxState.Victory;ClearObjects();events|=FoxEvent.Victory;break; }
                }
                for(int enemy=0;enemy<EnemyCount && Shots[i];enemy++)
                    if(Enemies[enemy] && Abs(ShotZ[i]-EnemyZ[enemy])<2 &&
                       Close(ShotX[i],ShotY[i],EnemyX[enemy],EnemyY[enemy],1.0f))
                    { Shots[i]=false;Enemies[enemy]=false;AddScore(25);events|=FoxEvent.EnemyHit; }
            }
            for(int i=0;i<BoltCount;i++)if(Bolts[i])
            {
                BoltX[i]+=BoltVx[i]*dt;BoltY[i]+=BoltVy[i]*dt;BoltZ[i]+=42*dt;
                if(Abs(BoltZ[i])<1 && Close(PlayerX,PlayerY,BoltX[i],BoltY[i],0.8f))
                { Bolts[i]=false;events|=Damage(); }
                else if(BoltZ[i]>5)Bolts[i]=false;
            }
            for(int i=0;i<RingCount;i++)if(Rings[i])
            {
                RingZ[i]+=Speed*dt;
                if(Abs(RingZ[i])<1.2f && Close(PlayerX,PlayerY,RingX[i],RingY[i],1.5f))
                { Rings[i]=false;if(Shield<MaximumShield)Shield++;Energy=1;AddScore(50);events|=FoxEvent.Ring; }
                else if(RingZ[i]>4)Rings[i]=false;
            }
        }
        return events;
    }
}
