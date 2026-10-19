// A menu direction changes immediately, then repeats using monotonic time.
// It has no native dependencies so the same rules run in host regression tests.
internal sealed class BrowserNavigation
{
    private int direction;
    private long repeatAt;
    internal void Reset(){direction=0;repeatAt=0;}
    internal int Update(bool connected,bool up,bool down,float stickY,long now)
    {
        int next=0;
        if(connected)
        {
            if(up!=down)next=up?-1:1;
            else if(!up)
            {
                // Hysteresis prevents stick noise near the threshold from
                // generating a fresh press on every frame.
                float threshold=direction==0?.55f:.35f;
                if(stickY>=threshold)next=-1;
                else if(stickY<=-threshold)next=1;
            }
        }
        if(next==0){Reset();return 0;}
        if(next!=direction){direction=next;repeatAt=now+350000;return next;}
        if(now<repeatAt)return 0;
        repeatAt=now+100000;return next;
    }
}
