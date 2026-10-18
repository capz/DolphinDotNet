using Dolphin.Input;

// Hardware is polled only by Program. Routers dispatch cached snapshots and
// suppress the held opener when a menu/pause context changes or reconnects.
internal sealed class SampleInput
{
    private sealed class Snapshot : InputContext
    {
        internal ControllerState State;
        internal Snapshot():base(false){}
        public override void HandleInput(InputFrame input){State=input.State;}
    }
    private readonly InputRouter router;
    private readonly Snapshot snapshot=new Snapshot();
    private readonly int port;
    private int mode=int.MinValue;
    private ButtonCaptureContext? capture;
    internal readonly ButtonBinding Primary;
    internal SampleInput(int port=0,ControllerButton primary=ControllerButton.A)
    {this.port=port;router=new InputRouter(port);Primary=new ButtonBinding(primary);router.Push(snapshot);}
    internal bool Capturing=>capture!=null&&capture.Status==ButtonCaptureStatus.Waiting;
    internal ControllerState Read(int nextMode=0)
    {
        if(mode!=nextMode){mode=nextMode;router.Clear();router.Push(snapshot);}
        snapshot.State=default;router.Update(Controller.GetState(port));return snapshot.State;
    }
    internal void Suppress()=>router.SuppressUntilRelease(Controller.GetState(port).HeldButtons);
    internal void CapturePrimary()
    {
        if(Capturing)return;
        capture=new ButtonCaptureContext(Primary,ControllerButton.B,ControllerButton.A|ControllerButton.X|ControllerButton.Y);
        router.Push(capture);
    }
}
