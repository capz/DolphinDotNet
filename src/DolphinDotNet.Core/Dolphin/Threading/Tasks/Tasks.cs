using System;
using System.Collections.Generic;
namespace Dolphin.Threading.Tasks
{
    /// <summary>Runtime-owned dispatch. Normal application awaits need no pumps.</summary>
    public static class Scheduler
    {
        private sealed class WorkQueue
        {
            private Action?[] items=new Action?[16];private int head,count;
            internal int Count=>count;
            internal void Add(Action action)
            {
                if(count==items.Length){var next=new Action?[items.Length*2];for(int i=0;i<count;i++)next[i]=items[(head+i)%items.Length];items=next;head=0;}
                items[(head+count)%items.Length]=action;count++;
            }
            internal Action Take(){var action=items[head]!;items[head]=null;head=(head+1)%items.Length;count--;return action;}
        }
        private static readonly WorkQueue applicationQueue=new WorkQueue();
        private static readonly WorkQueue backgroundQueue=new WorkQueue();
        /// <summary>Cooperative block budget; 1 retains the legacy clock-poll cadence.</summary>
        /// <summary>Recheck completion after service before parking. False is a validation control.</summary>
        public static bool CompletionAwareWaits {get;set;}=true;
        public static bool DeadlineWaits {set=>NativeAsync.AsyncDeadlineWaits(value?1:0);}
        /// <summary>Cumulative runtime diagnostics; index definitions are in docs/throughput.md.</summary>
        public static void CopyWaitStats(long[] destination)=>NativeAsync.AsyncWaitSnapshot(destination);
        public static void ResetWaitStats()=>NativeAsync.AsyncWaitReset();
        public static long WaitStat(int kind)=>NativeAsync.AsyncWaitStat(kind);
        public static bool ProductiveWaits {set=>NativeAsync.AsyncProductiveWaits(value?1:0);}
        public static int PollStride {set=>NativeAsync.AsyncPollStride(value);}
        public static long PollCalls=>NativeAsync.AsyncPollStat(0);
        public static long SlowPollCalls=>NativeAsync.AsyncPollStat(1);
        public static bool IsApplicationThread=>NativeAsync.AsyncContext()==0;
        internal static int Context=>NativeAsync.AsyncContext();
        public static void Post(Action action)=>Post(action,Context);
        internal static void PostBackground(Action action)=>Post(action,NativeAsync.AsyncRunning()?1:0);
        internal static void Post(Action action,int context)
        {
            if(action==null)throw new ArgumentNullException("action");
            var queue=context==0?applicationQueue:backgroundQueue;
            queue.Add(action);
            NativeAsync.AsyncWake();
        }
        // Kept for deterministic runtime tests and existing integrations.
        public static bool Pump()=>PumpContext(Context,1)!=0;
        public static int Pump(int maximumActions)=>PumpContext(Context,maximumActions);
        public static void RunUntilIdle(){while(Pump()){} }
        internal static int PumpContext(int context,int maximumActions)
        {
            if(maximumActions<1)throw new ArgumentOutOfRangeException("maximumActions");
            var queue=context==0?applicationQueue:backgroundQueue;
            int available=queue.Count,count=0;
            while(count<maximumActions&&count<available&&queue.Count!=0)
            {
                var action=queue.Take();count++;action();
            }
            return count;
        }
        internal static int Pending(int context)=>(context==0?applicationQueue:backgroundQueue).Count;
    }
    internal sealed class Continuation
    {
        internal readonly Action Action;
        internal readonly int Context;
        internal Continuation(Action action,int context){Action=action;Context=context;}
    }
    public class Task
    {
        private int state;
        private Exception? error;
        private List<Continuation>? continuations;
        public bool IsCompleted=>state!=0;
        public bool IsFaulted=>state==2;
        public bool IsCanceled=>state==3;
        public bool IsCompletedSuccessfully=>state==1;
        public static Task CompletedTask {get {var task=new Task();task.Complete();return task;}}
        internal void Complete(){Finish(1,null);}
        internal void Fail(Exception exception){Finish(exception is OperationCanceledException?3:2,exception);}
        private void Finish(int status,Exception? exception){if(state!=0)throw new InvalidOperationException("Task already completed.");error=exception;state=status;NativeAsync.AsyncWake();if(continuations!=null){foreach(var continuation in continuations)Scheduler.Post(continuation.Action,continuation.Context);continuations=null;}}
        internal void Continue(Action action)=>Continue(action,Scheduler.Context);
        internal void Continue(Action action,int context)
        {
            if(IsCompleted)Scheduler.Post(action,context);
            else{if(continuations==null)continuations=new List<Continuation>();continuations.Add(new Continuation(action,context));}
        }
        public void Wait()
        {
            while(!IsCompleted)
            {
                if(NativeAsync.AsyncRunning())
                {
                    if(!Scheduler.CompletionAwareWaits)NativeAsync.AsyncProgress();
                    else
                    {
                        NativeAsync.AsyncService();
                        // Core methods do not yield the managed gate between this
                        // check and park. A service callback may have settled us.
                        if(!IsCompleted)NativeAsync.AsyncWait();
                        else NativeAsync.AsyncWaitSkipped();
                    }
                }
                else if(!Scheduler.Pump())throw new InvalidOperationException("No queued work can complete this task.");
            }
            if(error!=null)throw error;
        }
        public Runtime.CompilerServices.ConfiguredTaskAwaitable ConfigureAwait(bool continueOnCapturedContext)=>new Runtime.CompilerServices.ConfiguredTaskAwaitable(this,continueOnCapturedContext);
        public Runtime.CompilerServices.TaskAwaiter GetAwaiter()=>new Runtime.CompilerServices.TaskAwaiter(this);
        public static Task Run(Action action)=>Run(action,default);
        public static Task Run(Action action,Dolphin.Threading.CancellationToken token){if(action==null)throw new ArgumentNullException("action");var task=new Task();Scheduler.PostBackground(()=>{try{token.ThrowIfCancellationRequested();action();task.Complete();}catch(Exception error){task.Fail(error);}});return task;}
        public static Task<T> Run<T>(Func<T> action)=>Run(action,default);
        public static Task<T> Run<T>(Func<T> action,Dolphin.Threading.CancellationToken token){if(action==null)throw new ArgumentNullException("action");var task=new Task<T>();Scheduler.PostBackground(()=>{try{token.ThrowIfCancellationRequested();task.SetResult(action());}catch(Exception error){task.Fail(error);}});return task;}
        public static Task<T> FromResult<T>(T result){var task=new Task<T>();task.SetResult(result);return task;}
        public static Task FromException(Exception error){if(error==null)throw new ArgumentNullException("error");var task=new Task();task.Fail(error);return task;}
        public static Task<T> FromException<T>(Exception error){if(error==null)throw new ArgumentNullException("error");var task=new Task<T>();task.Fail(error);return task;}
        public static Task FromCanceled(Dolphin.Threading.CancellationToken token){if(!token.IsCancellationRequested)throw new ArgumentOutOfRangeException("token");return FromException(new OperationCanceledException());}
        public static Task<T> FromCanceled<T>(Dolphin.Threading.CancellationToken token){if(!token.IsCancellationRequested)throw new ArgumentOutOfRangeException("token");return FromException<T>(new OperationCanceledException());}
        public static Task WhenAll(Task[] tasks){if(tasks==null)throw new ArgumentNullException("tasks");var copy=new Task[tasks.Length];for(var i=0;i<copy.Length;i++){if(tasks[i]==null)throw new ArgumentException("Null task.");copy[i]=tasks[i];}var work=new AllWork(copy);work.Start();return work.Result;}
        public static Task<Task> WhenAny(Task[] tasks){if(tasks==null)throw new ArgumentNullException("tasks");if(tasks.Length==0)throw new ArgumentException("No tasks.");foreach(var task in tasks)if(task==null)throw new ArgumentException("Null task.");foreach(var task in tasks)if(task.IsCompleted)return FromResult(task);var work=new AnyWork();foreach(var task in tasks){var candidate=task;candidate.Continue(()=>work.Finish(candidate));}return work.Result;}
        private sealed class AnyWork
        {
            internal readonly Task<Task> Result=new Task<Task>();
            internal void Finish(Task task){if(!Result.IsCompleted)Result.SetResult(task);}
        }
        private sealed class AllWork
        {
            private readonly Task[] tasks;private int remaining;private Exception? failure,cancellation;
            internal readonly Task Result=new Task();
            internal AllWork(Task[] tasks){this.tasks=tasks;remaining=tasks.Length;}
            internal void Start(){if(remaining==0){Result.Complete();return;}foreach(var task in tasks){var candidate=task;candidate.Continue(()=>Finish(candidate));}}
            private void Finish(Task task){try{task.Wait();}catch(OperationCanceledException error){cancellation=error;}catch(Exception error){if(failure==null)failure=error;}remaining--;if(remaining!=0)return;if(failure!=null)Result.Fail(failure);else if(cancellation!=null)Result.Fail(cancellation);else Result.Complete();}
        }
        public static Runtime.CompilerServices.YieldAwaitable Yield()=>new Runtime.CompilerServices.YieldAwaitable();
    }
    public class Task<T> : Task
    {
        private T result=default!;
        public T Result {get{Wait();return result;}}
        internal void SetResult(T value){result=value;Complete();}
        public new Runtime.CompilerServices.ConfiguredTaskAwaitable<T> ConfigureAwait(bool continueOnCapturedContext)=>new Runtime.CompilerServices.ConfiguredTaskAwaitable<T>(this,continueOnCapturedContext);
        public new Runtime.CompilerServices.TaskAwaiter<T> GetAwaiter()=>new Runtime.CompilerServices.TaskAwaiter<T>(this);
    }
    public sealed class TaskCompletionSource<T>
    {
        public Task<T> Task {get;}=new Task<T>();
        public void SetResult(T result)=>Task.SetResult(result);
        public void SetException(Exception exception)=>Task.Fail(exception);
        public void SetCanceled()=>Task.Fail(new OperationCanceledException());
    }
}
