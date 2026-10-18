using System;

internal enum GreenMenState { Title, Playing, Paused, WaveClear, GameOver }
[Flags]
internal enum GreenMenEvent { None=0, Shot=1, AlienHit=2, PlayerHit=4, Wave=8 }

/// <summary>Deterministic rules in logical 640 x 480 pixels; no platform dependencies.</summary>
internal sealed class GreenMenGame
{
    internal const int Columns=8, Rows=4, AlienCount=Columns*Rows, BombCount=4;
    internal const int ShieldCount=4, ShieldColumns=6, ShieldRows=4;
    internal const float PlayerY=420, AlienWidth=24, AlienHeight=18, ShieldY=354;
    internal readonly bool[] Aliens=new bool[AlienCount];
    internal readonly bool[] Bombs=new bool[BombCount];
    internal readonly float[] BombX=new float[BombCount], BombY=new float[BombCount];
    internal readonly bool[] Shields=new bool[ShieldCount*ShieldColumns*ShieldRows];
    internal float FormationX,FormationY,ShotX,ShotY;
    internal bool ShotActive;
    internal float PlayerX { get; private set; }
    internal int Score { get; private set; }
    internal int HighScore { get; private set; }
    internal void LoadBest(int score){if(score>HighScore)HighScore=score;}
    internal int Lives { get; private set; }
    internal int Wave { get; private set; }
    internal int Beat { get; private set; }
    internal float Invulnerable { get; private set; }
    internal GreenMenState State { get; private set; }
    private float marchTime,bombTime,shotTime,waveTime;
    private int direction=1;
    private GreenMenState resumeState;
    private uint random;

    internal GreenMenGame() { State=GreenMenState.Title; }
    internal int Remaining
    {
        get { int count=0;for(int i=0;i<AlienCount;i++)if(Aliens[i])count++;return count; }
    }
    internal float AlienX(int index)=>FormationX+(index%Columns)*48;
    internal float AlienY(int index)=>FormationY+(index/Columns)*34;
    internal static float ShieldX(int index)=>100+(index/24)*128+(index%6)*6;
    internal static float ShieldCellY(int index)=>ShieldY+((index%24)/6)*6;

    internal void Start()
    {
        Score=0;Lives=3;Wave=1;random=0x47524545;PlayerX=320;
        Invulnerable=0;State=GreenMenState.Playing;BeginWave();
    }
    internal void TogglePause()
    {
        if(State==GreenMenState.Playing || State==GreenMenState.WaveClear)
        { resumeState=State;State=GreenMenState.Paused; }
        else if(State==GreenMenState.Paused)State=resumeState;
    }
    internal void ReturnToTitle() { State=GreenMenState.Title;ClearShots(); }
    private void ClearShots()
    {
        ShotActive=false;
        for(int i=0;i<BombCount;i++)Bombs[i]=false;
    }
    private void BeginWave()
    {
        for(int i=0;i<AlienCount;i++)Aliens[i]=true;
        for(int i=0;i<Shields.Length;i++)
        {
            int row=(i%24)/6,column=i%6;
            Shields[i]=row==0?column>0 && column<5:
                row==1 || (row==2?(column<2 || column>3):(column==0 || column==5));
        }
        ClearShots();FormationX=140;FormationY=100+(Wave>6?6:Wave-1)*8;
        direction=1;marchTime=0;bombTime=0.8f;shotTime=0;Beat=0;
    }
    private static float Clamp(float x,float low,float high)=>Math.Clamp(x,low,high);
    private static bool Overlap(float x,float y,float w,float h,float a,float b,float aw,float ah)=>
        x<a+aw && x+w>a && y<b+ah && y+h>b;
    private bool HitShield(float x,float y,float width,float height)
    {
        for(int i=0;i<Shields.Length;i++)
            if(Shields[i] && Overlap(x,y,width,height,ShieldX(i),ShieldCellY(i),6,6))
            { Shields[i]=false;return true; }
        return false;
    }
    private void March()
    {
        float left=640,right=0;
        for(int i=0;i<AlienCount;i++)if(Aliens[i])
        {
            float x=AlienX(i);
            if(x<left)left=x;
            if(x+AlienWidth>right)right=x+AlienWidth;
        }
        if((direction>0 && right+12>610) || (direction<0 && left-12<30))
        { direction=-direction;FormationY+=14; }
        else FormationX+=direction*12;
        Beat++;
        for(int i=0;i<AlienCount;i++)if(Aliens[i])
        {
            float x=AlienX(i),y=AlienY(i);
            if(y+AlienHeight>=PlayerY-10) { Lives=0;State=GreenMenState.GameOver;ClearShots();return; }
            for(int cell=0;cell<Shields.Length;cell++)
                if(Shields[cell] && Overlap(x,y,AlienWidth,AlienHeight,ShieldX(cell),ShieldCellY(cell),6,6))
                    Shields[cell]=false;
        }
    }
    private void DropBomb()
    {
        int slot=-1;
        for(int i=0;i<BombCount;i++)if(!Bombs[i]) { slot=i;break; }
        if(slot<0)return;
        random=unchecked(random*1664525u+1013904223u);
        int first=(int)((random>>16)%Columns);
        // Only the lowest living alien in a column can fire.
        for(int n=0;n<Columns;n++)
        {
            int column=(first+n)%Columns;
            for(int row=Rows-1;row>=0;row--)
            {
                int alien=row*Columns+column;
                if(!Aliens[alien])continue;
                Bombs[slot]=true;BombX[slot]=AlienX(alien)+AlienWidth/2;
                BombY[slot]=AlienY(alien)+AlienHeight;return;
            }
        }
    }
    internal GreenMenEvent Update(float seconds,float movement,bool fire)
    {
        if(!(seconds>=0))throw new ArgumentOutOfRangeException("seconds");
        if(State!=GreenMenState.Playing && State!=GreenMenState.WaveClear)return GreenMenEvent.None;
        seconds=Clamp(seconds,0,0.05f);movement=Clamp(movement,-1,1);
        GreenMenEvent events=GreenMenEvent.None;
        // Small slices prevent fast projectiles tunnelling through shields or enemies.
        while(seconds>0)
        {
            float dt=seconds>1f/240?1f/240:seconds;seconds-=dt;
            if(State==GreenMenState.WaveClear)
            {
                waveTime-=dt;
                if(waveTime<=0) { Wave++;BeginWave();State=GreenMenState.Playing;events|=GreenMenEvent.Wave; }
                continue;
            }
            if(State!=GreenMenState.Playing)break;
            PlayerX=Clamp(PlayerX+movement*270*dt,30,610);
            Invulnerable=Clamp(Invulnerable-dt,0,2);shotTime-=dt;
            if(fire && !ShotActive && shotTime<=0)
            { ShotActive=true;ShotX=PlayerX;ShotY=PlayerY-18;shotTime=0.28f;events|=GreenMenEvent.Shot; }
            marchTime-=dt;
            if(marchTime<=0)
            {
                March();
                marchTime=Clamp(0.07f+Remaining*0.015f-(Wave-1)*0.035f,0.065f,0.55f);
            }
            if(State!=GreenMenState.Playing)break;
            if(ShotActive)
            {
                ShotY-=500*dt;
                if(ShotY<45 || HitShield(ShotX-2,ShotY,4,12))ShotActive=false;
                for(int i=AlienCount-1;i>=0 && ShotActive;i--)
                    if(Aliens[i] && Overlap(ShotX-2,ShotY,4,12,AlienX(i),AlienY(i),AlienWidth,AlienHeight))
                    {
                        Aliens[i]=false;ShotActive=false;Score+=(Rows-i/Columns)*10;
                        if(Score>HighScore)HighScore=Score;
                        events|=GreenMenEvent.AlienHit;
                        if(Remaining==0)
                        { State=GreenMenState.WaveClear;waveTime=1.1f;ClearShots(); }
                    }
            }
            if(State!=GreenMenState.Playing)continue;
            bombTime-=dt;
            if(bombTime<=0) { DropBomb();bombTime=Clamp(0.85f-Wave*0.055f,0.24f,0.8f); }
            for(int i=0;i<BombCount;i++)if(Bombs[i])
            {
                BombY[i]+=(150+(Wave>12?12:Wave)*12)*dt;
                if(BombY[i]>452 || HitShield(BombX[i]-2,BombY[i],4,10)) { Bombs[i]=false;continue; }
                if(Invulnerable<=0 && Overlap(BombX[i]-2,BombY[i],4,10,PlayerX-14,PlayerY-8,28,16))
                {
                    Lives--;Invulnerable=1.5f;PlayerX=320;ClearShots();events|=GreenMenEvent.PlayerHit;
                    if(Lives==0)State=GreenMenState.GameOver;
                }
            }
        }
        return events;
    }
}
