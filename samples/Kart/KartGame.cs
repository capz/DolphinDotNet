using System;

// Distances in metres, angles in radians, fixed 60 Hz local simulation.
internal sealed class KartGame
{
    internal const float Radius=52f,HalfWidth=7f,Step=1f/60;
    internal float X=Radius,Z,Heading,Speed,Height,VerticalSpeed,DriftCharge,Boost,Time,Countdown,Stun,Invulnerable;
    internal readonly KartItems Items;
    internal float RaceTime=-1;
    internal float Elapsed=>RaceTime>=0?RaceTime:Time;
    internal int Lap,NextGate=1;
    internal bool Finished,Gliding,Drifting;
    private float accumulator;
    private bool previousDrift;
    internal KartGame(int seed=0){Heading=0;Items=new KartItems(seed);}
    internal static float Clamp(float x,float low,float high)=>Math.Clamp(x,low,high);
    internal static float Wrap(float angle)
    { while(angle>MathF.PI)angle-=2*MathF.PI;while(angle< -MathF.PI)angle+=2*MathF.PI;return angle; }
    internal static float Distance(float x,float z)=>MathF.Sqrt(x*x+z*z);
    internal float Angle=>MathF.Atan2(Z,X)<0?MathF.Atan2(Z,X)+2*MathF.PI:MathF.Atan2(Z,X);
    internal void Update(float seconds,float steering,bool throttle,bool brake,bool drift,bool glide)
    {
        accumulator+=Clamp(seconds,0,.1f);
        while(accumulator>=Step){Tick(steering,throttle,brake,drift,glide);accumulator-=Step;}
    }
    private void Tick(float steering,bool throttle,bool brake,bool drift,bool glide)
    {
        Stun=Clamp(Stun-Step,0,2);Invulnerable=Clamp(Invulnerable-Step,0,2);
        Items.Update(this,Step);
        if(Finished)return;
        if(Countdown>0){Countdown=Clamp(Countdown-Step,0,3);return;}
        Time+=Step;steering=Clamp(steering,-1,1);
        if(Stun>0){throttle=false;steering=0;drift=false;glide=false;}
        float distance=Distance(X,Z),off=MathF.Abs(distance-Radius);
        Drifting=drift && Speed>9 && off<HalfWidth && Height<.1f;
        if(Drifting && MathF.Abs(steering)>.25f)DriftCharge=Clamp(DriftCharge+Step,0,1.4f);
        if(previousDrift && !drift){if(DriftCharge>.65f)Boost=1.2f;DriftCharge=0;}
        if(!drift && !previousDrift)DriftCharge=0;
        previousDrift=drift;
        Boost=Clamp(Boost-Step,0,2);
        Gliding=glide && Height>1 && VerticalSpeed<3;
        float top=off>HalfWidth?10:Boost>0?36:26;
        Speed+=((throttle?16:0)-(brake?30:0)-3-Speed*.12f)*Step;
        Speed=Clamp(Speed,0,top);
        Heading=Wrap(Heading-steering*(Drifting?1.65f:1.15f)*Clamp(Speed/9,0,1)*Step);
        X+=MathF.Sin(Heading)*Speed*Step;Z+=MathF.Cos(Heading)*Speed*Step;
        VerticalSpeed-=(Gliding?2.3f:14f)*Step;
        Height+=VerticalSpeed*Step;
        if(Height<0){Height=0;VerticalSpeed=0;Gliding=false;}
        float angle=Angle;
        // Ordered quarter gates reject reverse crossings and start-line farming.
        int gate=(int)(angle/(MathF.PI*.5f));
        float tangent=MathF.Atan2(-MathF.Sin(angle),MathF.Cos(angle));
        bool forward=MathF.Cos(Wrap(Heading-tangent))>.2f;
        if(off<HalfWidth && forward && gate==NextGate)
        {
            if(gate==0){Lap++;if(Lap>=3)Finished=true;}
            NextGate=(NextGate+1)%4;
        }
        // Two boost strips; the second launches a small gliding jump.
        bool pad=angle>.70f && angle<.83f || angle>4.65f && angle<4.78f;
        if(pad && off<3 && Height==0 && Speed>5)
        { Boost=1.4f;if(angle>4)VerticalSpeed=9; }
        if(Distance(X,Z)>Radius+HalfWidth+13 || Distance(X,Z)<Radius-HalfWidth-13)Rescue();
    }
    internal bool Hit()
    {
        if(Countdown>0 || Finished || Invulnerable>0)return false;
        Stun=1.1f;Invulnerable=1.8f;Speed*=.25f;Boost=DriftCharge=0;
        Drifting=false;return true;
    }
    internal void Rescue()
    {
        float angle=Angle;X=MathF.Cos(angle)*Radius;Z=MathF.Sin(angle)*Radius;
        Heading=MathF.Atan2(-MathF.Sin(angle),MathF.Cos(angle));Speed=0;
        Height=VerticalSpeed=Boost=DriftCharge=0;Drifting=Gliding=previousDrift=false;
    }
}
