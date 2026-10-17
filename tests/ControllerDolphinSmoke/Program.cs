using Dolphin.Input;

// The host injects one case per eight VIDEO frames. Expectations are checked
// here, through the public managed API, rather than against injected RAM.
internal static class Program
{
    private static int currentCase;
    private static readonly ControllerButton[] Buttons = {
        ControllerButton.A, ControllerButton.B, ControllerButton.X, ControllerButton.Y,
        ControllerButton.Z, ControllerButton.Start, ControllerButton.L, ControllerButton.R,
        ControllerButton.DPadUp, ControllerButton.DPadDown, ControllerButton.DPadLeft, ControllerButton.DPadRight
    };

    private static int Main()
    {
        for (int p=0;p<4;p++)
            Controller.Configure(p,new ControllerOptions { StickMaximum=127, CStickMaximum=127, TriggerMinimum=0, TriggerMaximum=255 });
        int error=Check(0,0,0,0,-1,0,true);
        if(error!=0)return error;
        for(int port=0;port<4;port++)
        {
            for(int i=0;i<Buttons.Length;i++)
            {
                int button=(int)Buttons[i];
                // Dolphin's digital trigger click also fully depresses its analog channel.
                int analog=Buttons[i]==ControllerButton.L?4:Buttons[i]==ControllerButton.R?5:-1;
                error=Check(port,button,button,0,analog,1);if(error!=0)return error;
                error=Check(port,button,0,0,analog,1);if(error!=0)return error;
                error=Check(port,0,0,button);if(error!=0)return error;
            }
            int chord=(int)(ControllerButton.A|ControllerButton.B|ControllerButton.X|ControllerButton.Y|ControllerButton.Z|ControllerButton.Start);
            error=Check(port,chord,chord,0);if(error!=0)return error;
            error=Check(port,0,0,chord);if(error!=0)return error;
            for(int axis=0;axis<4;axis++)
                for(int value=-2;value<=2;value++)
                { error=Check(port,0,0,0,axis,value/2f);if(error!=0)return error; }
            for(int axis=4;axis<6;axis++)
                for(int value=0;value<=4;value++)
                { error=Check(port,0,0,0,axis,value/4f);if(error!=0)return error; }
            // Both sticks diagonal, then inside their radial dead zones.
            error=Check(port,0,0,0,6,1);if(error!=0)return error;
            error=Check(port,0,0,0,7,0);if(error!=0)return error;
            error=Check(port,0,0,0);if(error!=0)return error;
        }
        return 0;
    }

    private static int PropertyMask(ControllerState s) =>
        (s.A.IsHeld?0x100:0)|(s.B.IsHeld?0x200:0)|(s.X.IsHeld?0x400:0)|(s.Y.IsHeld?0x800:0)|
        (s.Z.IsHeld?0x10:0)|(s.Start.IsHeld?0x1000:0)|(s.L.IsHeld?0x40:0)|(s.R.IsHeld?0x20:0)|
        (s.DPadUp.IsHeld?8:0)|(s.DPadDown.IsHeld?4:0)|(s.DPadLeft.IsHeld?1:0)|(s.DPadRight.IsHeld?2:0);

    private static int Check(int port,int held,int pressed,int released,int axis=-1,float value=0,bool initial=false)
    {
        currentCase++;
        int down=0,up=0;
        ControllerState last=default;
        for(int frame=0;frame<8;frame++)
        {
            Controller.Update();
            for(int p=0;p<4;p++)
            {
                var state=Controller.GetState(p);
                if(PropertyMask(state)!=(int)state.HeldButtons)return currentCase*100+1;
                if(p!=port && ((int)state.HeldButtons!=0 || (int)state.PressedButtons!=0 || (int)state.ReleasedButtons!=0))return currentCase*100+2;
                if(frame==7 && !state.IsConnected)return currentCase*100+3;
                if(p!=port && frame==7 && !Axes(state,-1,0))return currentCase*100+4;
                if(p==port)
                {
                    down|=(int)state.PressedButtons;up|=(int)state.ReleasedButtons;last=state;
                    // Repeated queries cannot consume transitions or alter copies.
                    var again=Controller.GetState(p);
                    if(again.PressedButtons!=state.PressedButtons || again.ReleasedButtons!=state.ReleasedButtons)return currentCase*100+5;
                    for(int b=0;b<Buttons.Length;b++)
                    {
                        var button=state[Buttons[b]];int mask=(int)Buttons[b];
                        if(button.WasPressed!=(((int)state.PressedButtons&mask)!=0) || button.WasReleased!=(((int)state.ReleasedButtons&mask)!=0))return currentCase*100+6;
                    }
                }
            }
        }
        if((int)last.HeldButtons!=held)return currentCase*100+7;
        if(!initial && (down!=pressed || up!=released))return currentCase*100+8;
        if(!Axes(last,axis,value))return currentCase*100+9;
        if((int)last.PressedButtons!=0 || (int)last.ReleasedButtons!=0)return currentCase*100+10;
        return 0;
    }

    private static bool Axes(ControllerState state,int active,float value)
    {
        for(int axis=0;axis<6;axis++)
        {
            float actual=axis==0?state.Stick.X:axis==1?state.Stick.Y:axis==2?state.CStick.X:axis==3?state.CStick.Y:axis==4?state.LeftTrigger:state.RightTrigger;
            float magnitude=actual<0?-actual:actual;
            if(active==6 && axis<4) { if(actual<0.65f || actual>0.76f)return false; }
            else if(axis!=active || value==0) { if(magnitude>0.025f)return false; }
            else if(axis>=4) { if(actual<value-0.025f || actual>value+0.025f)return false; }
            else if(value==1 || value==-1) { if(actual*value<0.95f || magnitude>1.001f)return false; }
            else if(actual*value<=0 || magnitude<0.38f || magnitude>0.44f)return false;
        }
        return true;
    }
}
