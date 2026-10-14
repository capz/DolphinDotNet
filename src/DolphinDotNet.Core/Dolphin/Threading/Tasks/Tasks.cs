using System;
using System.Collections.Generic;
namespace Dolphin.Threading.Tasks
{
    /// <summary>Single-threaded cooperative queue. Pump on the application thread.</summary>
    public static class Scheduler
    {
        private static readonly List<Action> queue=new List<Action>();
        public static void Post(Action action){if(action==null)throw new ArgumentNullException("action");queue.Add(action);}
        public static bool Pump(){if(queue.Count==0)return false;var action=queue[0];queue.RemoveAt(0);action();return true;}
        public static int Pump(int maximumActions){if(maximumActions<1)throw new ArgumentOutOfRangeException("maximumActions");int available=queue.Count,count=0;while(count<maximumActions&&count<available&&Pump())count++;return count;}
        public static void RunUntilIdle(){while(Pump()){} }
    }
    public class Task
    {
        private int state;
        private Exception? error;
        private List<Action>? continuations;
        public bool IsCompleted=>state!=0;
        public bool IsFaulted=>state==2;
        public bool IsCanceled=>state==3;
        public bool IsCompletedSuccessfully=>state==1;
        public static Task CompletedTask {get {var task=new Task();task.Complete();return task;}}
        internal void Complete(){Finish(1,null);}
        internal void Fail(Exception exception){Finish(exception is OperationCanceledException?3:2,exception);}
        private void Finish(int status,Exception? exception){if(state!=0)throw new InvalidOperationException("Task already completed.");error=exception;state=status;if(continuations!=null){foreach(var continuation in continuations)Scheduler.Post(continuation);continuations=null;}}
        internal void Continue(Action action){if(IsCompleted)Scheduler.Post(action);else{if(continuations==null)continuations=new List<Action>();continuations.Add(action);}}
        public void Wait(){while(!IsCompleted){if(!Scheduler.Pump())throw new InvalidOperationException("No queued work can complete this task.");}if(error!=null)throw error;}
        public Runtime.CompilerServices.ConfiguredTaskAwaitable ConfigureAwait(bool continueOnCapturedContext)=>new Runtime.CompilerServices.ConfiguredTaskAwaitable(this);
        public Runtime.CompilerServices.TaskAwaiter GetAwaiter()=>new Runtime.CompilerServices.TaskAwaiter(this);
        public static Task Run(Action action)=>Run(action,default);
        public static Task Run(Action action,Dolphin.Threading.CancellationToken token){if(action==null)throw new ArgumentNullException("action");var task=new Task();Scheduler.Post(()=>{try{token.ThrowIfCancellationRequested();action();task.Complete();}catch(Exception error){task.Fail(error);}});return task;}
        public static Task<T> Run<T>(Func<T> action)=>Run(action,default);
        public static Task<T> Run<T>(Func<T> action,Dolphin.Threading.CancellationToken token){if(action==null)throw new ArgumentNullException("action");var task=new Task<T>();Scheduler.Post(()=>{try{token.ThrowIfCancellationRequested();task.SetResult(action());}catch(Exception error){task.Fail(error);}});return task;}
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
        public new Runtime.CompilerServices.ConfiguredTaskAwaitable<T> ConfigureAwait(bool continueOnCapturedContext)=>new Runtime.CompilerServices.ConfiguredTaskAwaitable<T>(this);
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
