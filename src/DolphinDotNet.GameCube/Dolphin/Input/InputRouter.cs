using System;

namespace Dolphin.Input;

/// <summary>A bounded, allocation-free-per-update context stack for one player. Use on the application executor.</summary>
public sealed class InputRouter
{
    private readonly InputContext?[] contexts;
    private readonly InputFrame frame = new InputFrame();
    private ControllerState current;
    private ControllerButton blocked;
    private int count, revision;
    private bool updating, changing;
    public int Port { get; }
    public int Count => count;
    internal ControllerButton HeldButtons => current.HeldButtons;
    public InputContext? Current => count==0 ? null : contexts[count-1];
    public InputRouter(int port=0, int capacity=16)
    {
        Controller.ValidatePort(port);
        if (capacity<=0) throw new ArgumentOutOfRangeException("capacity");
        Port=port;
        contexts=new InputContext?[capacity];
    }
    public void Push(InputContext context, bool suppressHeldButtons=true)
    {
        CheckMutation();
        if (context==null) throw new ArgumentNullException("context");
        if (context.Owner!=null) throw new InvalidOperationException("Context already belongs to a router.");
        if (count==contexts.Length) throw new InvalidOperationException("Input context capacity reached.");
        if (suppressHeldButtons) SuppressUntilRelease(current.HeldButtons);
        contexts[count++]=context;
        context.Owner=this;
        revision++;
        RefreshActive();
    }
    public InputContext? Pop(bool suppressHeldButtons=true)
    {
        CheckMutation();
        var context=Current;
        if (context!=null) Remove(context,suppressHeldButtons);
        return context;
    }
    /// <summary>Remove a particular context, including one below a non-modal overlay.</summary>
    public bool Remove(InputContext context, bool suppressHeldButtons=true)
    {
        CheckMutation();
        if (context==null) throw new ArgumentNullException("context");
        if (context.Owner!=this) return false;
        int index=0;
        while (contexts[index]!=context) index++;
        if (suppressHeldButtons) SuppressUntilRelease(current.HeldButtons);
        count--;
        for (int i=index;i<count;i++) contexts[i]=contexts[i+1];
        contexts[count]=null;
        revision++;
        changing=true;
        try
        {
            if (context.Active) { context.Active=false; context.OnDeactivated(); }
        }
        finally
        {
            context.Owner=null;
            changing=false;
            RefreshActive();
        }
        return true;
    }
    public void Clear(bool suppressHeldButtons=true)
    {
        CheckMutation();
        if (suppressHeldButtons) SuppressUntilRelease(current.HeldButtons);
        // Remove top down without reactivating intermediate contexts.
        revision++;
        changing=true;
        try
        {
            while (count>0)
            {
                var context=contexts[--count]!;
                contexts[count]=null;
                try
                {
                    if (context.Active) { context.Active=false; context.OnDeactivated(); }
                }
                finally { context.Owner=null; }
            }
        }
        finally { changing=false; }
    }
    /// <summary>Hide these buttons until physical release, including their eventual release edge.</summary>
    public void SuppressUntilRelease(ControllerButton buttons)
    {
        ControllerState.ValidateButtons(buttons);
        blocked|=buttons & current.HeldButtons;
        if (updating) frame.Consume(buttons);
    }
    /// <summary>Read the shared cache. Call once after Controller.Update(), never once per context.</summary>
    public void Update() => Update(Controller.GetState(Port));
    /// <summary>Dispatch an existing snapshot, without polling hardware.</summary>
    public void Update(ControllerState state)
    {
        if (updating || changing) throw new InvalidOperationException("Input dispatch is not reentrant.");
        current=state;
        // Reconnection must not turn an already held button into a new logical action.
        if (state.WasConnected) blocked|=state.HeldButtons;
        var hidden=blocked;
        blocked &= state.HeldButtons;
        frame.Reset(state.Without(hidden));
        int version=revision;
        updating=true;
        try
        {
            if (state.WasDisconnected)
            {
                // Notify every active owner before a handler can mutate the stack.
                changing=true;
                try
                {
                    for (int i=count-1;i>=0;i--)
                        if (contexts[i]!.Active) contexts[i]!.OnDisconnected();
                }
                finally { changing=false; }
            }
            for (int i=count-1;i>=0;i--)
            {
                var context=contexts[i]!;
                context.HandleInput(frame);
                // A newly exposed context must never receive the same opening/closing press.
                if (version!=revision || context.IsModal) return;
            }
        }
        finally { updating=false; }
    }
    private void CheckMutation()
    {
        if (changing) throw new InvalidOperationException("Cannot change contexts from lifecycle callbacks.");
    }
    private void RefreshActive()
    {
        int first=count-1;
        while (first>0 && !contexts[first]!.IsModal) first--;
        changing=true;
        try
        {
            for (int i=count-1;i>=0;i--)
            {
                var context=contexts[i]!;
                if (context.Active && i<first) { context.Active=false; context.OnDeactivated(); }
            }
            for (int i=first;i<count && i>=0;i++)
            {
                var context=contexts[i]!;
                if (!context.Active) { context.Active=true; context.OnActivated(); }
            }
        }
        finally { changing=false; }
    }
}
