using System;
using System.Runtime.CompilerServices;

namespace DolphinDotNet.Threading;

public sealed class GameTask
{
    private Action? continuation;
    private Exception? exception;
    public bool IsCompleted { get; private set; }
    public GameTaskAwaiter GetAwaiter() => new(this);
    internal void Complete() { if (IsCompleted) return; IsCompleted=true; var c=continuation; continuation=null; if(c!=null) GameTaskScheduler.Post(c); }
    internal void Fail(Exception error) { exception=error; Complete(); }
    internal void OnCompleted(Action action) { if(IsCompleted) GameTaskScheduler.Post(action); else continuation+=action; }
    internal void GetResult() { if(exception!=null) throw exception; }
    public static GameTask Yield() { var task=new GameTask(); GameTaskScheduler.Post(task.Complete); return task; }
}

public readonly struct GameTaskAwaiter : ICriticalNotifyCompletion
{
    private readonly GameTask task;
    internal GameTaskAwaiter(GameTask task) => this.task=task;
    public bool IsCompleted => task.IsCompleted;
    public void OnCompleted(Action continuation) => task.OnCompleted(continuation);
    public void UnsafeOnCompleted(Action continuation) => task.OnCompleted(continuation);
    public void GetResult() => task.GetResult();
}

public static class GameTaskScheduler
{
    private sealed class Work { internal readonly Action Action; internal Work? Next; internal Work(Action action)=>Action=action; }
    private static Work? head,tail;
    public static void Post(Action action) { var item=new Work(action); if(tail==null) head=tail=item; else { tail.Next=item; tail=item; } }
    public static bool RunOne() { var item=head; if(item==null)return false; head=item.Next;if(head==null)tail=null;item.Action();return true; }
    public static void RunReady() { while(RunOne()){} }
}
