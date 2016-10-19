using System.Collections.Generic;
using System.Reactive.Subjects;

namespace System.Reactive.Backpressure
{
    public static class ExtensionMethods
    {
        /// <summary>
        /// Back pressure-aware method. If not "running" and input even occurs, calls selector and returns evens from it's observable.
        /// All events that occur on input while previously selected observable is running, are buffered. After previous observable completes, new is selected with accumulated buffer.
        /// Only calls selector if subscribed to.
        /// </summary>
        public static IObservable<TResult> BufferWhileRunning<TValue, TResult>(this IObservable<TValue> input, Func<IEnumerable<TValue>, IObservable<TResult>> selector)
        {
            return new BufferWhileRunningClass<TValue, TResult>(input, selector).Run();
        }

        private class BufferWhileRunningClass<TValue, TResult>
        {
            private IObservable<TValue> input;
            private Func<IEnumerable<TValue>, IObservable<TResult>> selector;
            private Subject<TResult> subject;
            private IDisposable inputSubscription;

            private bool IsRunning { get { return currentSubscription != null; } }

            private IDisposable currentSubscription;

            List<TValue> buffer;

            public BufferWhileRunningClass(IObservable<TValue> input, Func<IEnumerable<TValue>, IObservable<TResult>> selector)
            {
                this.input = input;
                this.selector = selector;
                buffer = new List<TValue>();

                subject = new Subject<TResult>();
            }

            public IObservable<TResult> Run()
            {
                inputSubscription = input.Subscribe(OnNextInput, OnErrorInput, OnCompletedInput);

                return subject;
            }

            private void OnNextInput(TValue val)
            {
                buffer.Add(val);

                if (!IsRunning)
                {
                    StartNext();
                }
            }

            private void OnCompletedInput()
            {
                subject.OnCompleted();
                inputSubscription.Dispose();

                if (IsRunning)
                {
                    UnsubscribeCurrent();
                }
            }

            private void OnErrorInput(Exception ex)
            {
                inputSubscription.Dispose();

                subject.OnError(ex);
            }

            private void OnNextSelected(TResult res)
            {
                subject.OnNext(res);
            }

            private void OnCompletedSelected()
            {
                UnsubscribeCurrent();

                StartNext();
            }

            private void OnErrorSelected(Exception ex)
            {
                inputSubscription.Dispose();

                UnsubscribeCurrent();

                subject.OnError(ex);
            }

            /// <summary>
            /// Starts next observable if possible or needed.
            /// </summary>
            private void StartNext()
            {
                var values = buffer.ToArray();
                buffer.Clear();

                if (values.Length == 0)
                    return;

                if (!subject.HasObservers)
                    return;

                var currentObservable = selector(values);

                currentSubscription = currentObservable.Subscribe(OnNextSelected, OnErrorSelected, OnCompletedSelected);
            }

            private void UnsubscribeCurrent()
            {
                currentSubscription.Dispose();
                currentSubscription = null;
            }
        }
    }
}
