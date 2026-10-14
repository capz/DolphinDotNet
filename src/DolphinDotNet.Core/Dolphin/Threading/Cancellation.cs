using System;
using System.Collections.Generic;
namespace Dolphin.Threading
{
    public sealed class CancellationTokenSource : IDisposable
    {
        internal bool canceled;
        public CancellationToken Token=>new CancellationToken(this);
        public bool IsCancellationRequested=>canceled;
        public void Cancel(){canceled=true;}
        public void Dispose(){}
    }
    public struct CancellationToken
    {
        private CancellationTokenSource? source;
        internal CancellationToken(CancellationTokenSource source){this.source=source;}
        public CancellationToken(bool canceled){source=canceled?new CancellationTokenSource():null;if(source!=null)source.Cancel();}
        public static CancellationToken None=>default;
        public bool CanBeCanceled=>source!=null;
        public bool IsCancellationRequested=>source!=null&&source.IsCancellationRequested;
        public void ThrowIfCancellationRequested(){if(IsCancellationRequested)throw new OperationCanceledException();}
    }
}
