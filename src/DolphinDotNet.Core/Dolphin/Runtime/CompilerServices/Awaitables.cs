using System;
using System.Collections.Generic;
namespace Dolphin.Runtime.CompilerServices
{
    public interface INotifyCompletion {void OnCompleted(Action continuation);}
    public interface ICriticalNotifyCompletion : INotifyCompletion {void UnsafeOnCompleted(Action continuation);}
    public interface IAsyncStateMachine {void MoveNext();void SetStateMachine(IAsyncStateMachine stateMachine);}
    public struct TaskAwaiter : ICriticalNotifyCompletion, System.Runtime.CompilerServices.ICriticalNotifyCompletion
    {
        private Dolphin.Threading.Tasks.Task task;
        internal TaskAwaiter(Dolphin.Threading.Tasks.Task task){this.task=task;}
        public bool IsCompleted=>task.IsCompleted;
        public void GetResult()=>task.Wait();
        public void OnCompleted(Action continuation)=>task.Continue(continuation);
        public void UnsafeOnCompleted(Action continuation)=>OnCompleted(continuation);
    }
    public struct TaskAwaiter<T> : ICriticalNotifyCompletion, System.Runtime.CompilerServices.ICriticalNotifyCompletion
    {
        private Dolphin.Threading.Tasks.Task<T> task;
        internal TaskAwaiter(Dolphin.Threading.Tasks.Task<T> task){this.task=task;}
        public bool IsCompleted=>task.IsCompleted;
        public T GetResult()=>task.Result;
        public void OnCompleted(Action continuation)=>task.Continue(continuation);
        public void UnsafeOnCompleted(Action continuation)=>OnCompleted(continuation);
    }
    public struct ConfiguredTaskAwaitable
    {
        private Dolphin.Threading.Tasks.Task task;
        private bool capture;
        internal ConfiguredTaskAwaitable(Dolphin.Threading.Tasks.Task task,bool capture){this.task=task;this.capture=capture;}
        public ConfiguredTaskAwaiter GetAwaiter()=>new ConfiguredTaskAwaiter(task,capture);
        public struct ConfiguredTaskAwaiter : ICriticalNotifyCompletion, System.Runtime.CompilerServices.ICriticalNotifyCompletion
        {
            private Dolphin.Threading.Tasks.Task task;
            private bool capture;
            internal ConfiguredTaskAwaiter(Dolphin.Threading.Tasks.Task task,bool capture){this.task=task;this.capture=capture;}
            public bool IsCompleted=>task.IsCompleted;
            public void GetResult()=>task.Wait();
            public void OnCompleted(Action action)=>task.Continue(action,capture||!Dolphin.Threading.Tasks.NativeAsync.AsyncRunning()?Dolphin.Threading.Tasks.Scheduler.Context:1);
            public void UnsafeOnCompleted(Action action)=>OnCompleted(action);
        }
    }
    public struct ConfiguredTaskAwaitable<T>
    {
        private Dolphin.Threading.Tasks.Task<T> task;
        private bool capture;
        internal ConfiguredTaskAwaitable(Dolphin.Threading.Tasks.Task<T> task,bool capture){this.task=task;this.capture=capture;}
        public ConfiguredTaskAwaiter GetAwaiter()=>new ConfiguredTaskAwaiter(task,capture);
        public struct ConfiguredTaskAwaiter : ICriticalNotifyCompletion, System.Runtime.CompilerServices.ICriticalNotifyCompletion
        {
            private Dolphin.Threading.Tasks.Task<T> task;
            private bool capture;
            internal ConfiguredTaskAwaiter(Dolphin.Threading.Tasks.Task<T> task,bool capture){this.task=task;this.capture=capture;}
            public bool IsCompleted=>task.IsCompleted;
            public T GetResult()=>task.Result;
            public void OnCompleted(Action action)=>task.Continue(action,capture||!Dolphin.Threading.Tasks.NativeAsync.AsyncRunning()?Dolphin.Threading.Tasks.Scheduler.Context:1);
            public void UnsafeOnCompleted(Action action)=>OnCompleted(action);
        }
    }
    public struct YieldAwaitable
    {
        public YieldAwaiter GetAwaiter()=>new YieldAwaiter();
        public struct YieldAwaiter : ICriticalNotifyCompletion, System.Runtime.CompilerServices.ICriticalNotifyCompletion
        {
            public bool IsCompleted=>false;
            public void GetResult(){}
            public void OnCompleted(Action continuation)=>Dolphin.Threading.Tasks.Scheduler.Post(continuation);
            public void UnsafeOnCompleted(Action continuation)=>OnCompleted(continuation);
        }
    }
    internal sealed class StateMachineBox
    {
        internal IAsyncStateMachine? machine;
        internal void MoveNext()=>machine!.MoveNext();
    }
    public struct AsyncTaskMethodBuilder
    {
        private Dolphin.Threading.Tasks.Task task;
        private StateMachineBox box;
        public static AsyncTaskMethodBuilder Create(){var builder=new AsyncTaskMethodBuilder();builder.task=new Dolphin.Threading.Tasks.Task();builder.box=new StateMachineBox();return builder;}
        public Dolphin.Threading.Tasks.Task Task=>task;
        public void SetResult()=>task.Complete();
        public void SetException(Exception exception)=>task.Fail(exception);
        public void SetStateMachine(IAsyncStateMachine stateMachine){box.machine=stateMachine;}
        public void Start<T>(ref T machine) where T:IAsyncStateMachine {machine.MoveNext();}
        public void AwaitOnCompleted<TAwaiter,TMachine>(ref TAwaiter awaiter,ref TMachine machine) where TAwaiter:INotifyCompletion where TMachine:IAsyncStateMachine {if(box.machine==null)box.machine=machine;awaiter.OnCompleted(box.MoveNext);}
        public void AwaitUnsafeOnCompleted<TAwaiter,TMachine>(ref TAwaiter awaiter,ref TMachine machine) where TAwaiter:ICriticalNotifyCompletion where TMachine:IAsyncStateMachine {if(box.machine==null)box.machine=machine;awaiter.UnsafeOnCompleted(box.MoveNext);}
    }
    public struct AsyncTaskMethodBuilder<T>
    {
        private Dolphin.Threading.Tasks.Task<T> task;
        private StateMachineBox box;
        public static AsyncTaskMethodBuilder<T> Create(){var builder=new AsyncTaskMethodBuilder<T>();builder.task=new Dolphin.Threading.Tasks.Task<T>();builder.box=new StateMachineBox();return builder;}
        public Dolphin.Threading.Tasks.Task<T> Task=>task;
        public void SetResult(T value)=>task.SetResult(value);
        public void SetException(Exception exception)=>task.Fail(exception);
        public void SetStateMachine(IAsyncStateMachine stateMachine){box.machine=stateMachine;}
        public void Start<TMachine>(ref TMachine machine) where TMachine:IAsyncStateMachine {machine.MoveNext();}
        public void AwaitOnCompleted<TAwaiter,TMachine>(ref TAwaiter awaiter,ref TMachine machine) where TAwaiter:INotifyCompletion where TMachine:IAsyncStateMachine {if(box.machine==null)box.machine=machine;awaiter.OnCompleted(box.MoveNext);}
        public void AwaitUnsafeOnCompleted<TAwaiter,TMachine>(ref TAwaiter awaiter,ref TMachine machine) where TAwaiter:ICriticalNotifyCompletion where TMachine:IAsyncStateMachine {if(box.machine==null)box.machine=machine;awaiter.UnsafeOnCompleted(box.MoveNext);}
    }
}
