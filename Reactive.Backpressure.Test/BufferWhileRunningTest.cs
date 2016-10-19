using Microsoft.Reactive.Testing;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace System.Reactive.Backpressure
{
    [TestFixture]
    [Timeout(1000)]
    public class BufferWhileRunningTest : ReactiveTest
    {
        /// <summary>
        /// Array of items. Needed for equality check.
        /// </summary>
        /// <typeparam name="TValue"></typeparam>
        struct Items<TValue>
        {
            readonly TValue[] items;

            public Items(IEnumerable<TValue> items)
            {
                this.items = items.ToArray();
            }

            public Items(params TValue[] items)
            {
                this.items = items;
            }

            public override bool Equals(object obj)
            {
                Items<TValue> other = (Items<TValue>)obj;

                return Enumerable.Zip(other.items, this.items, (a, b) => a.Equals(b)).All(x => x);
            }
        }

        struct SelectorCall
        {
            private long clock;
            private IEnumerable<int> items;
            private ITestableObservable<Items<int>> selectedObservable;

            public SelectorCall(long clock, IEnumerable<int> items, ITestableObservable<Items<int>> selectedObservable)
            {
                this.clock = clock;
                this.items = items;
                this.selectedObservable = selectedObservable;
            }

            public long Clock
            {
                get
                {
                    return clock;
                }
            }

            public IEnumerable<int> Items
            {
                get
                {
                    return items;
                }
            }

            public ITestableObservable<Items<int>> SelectedObservable
            {
                get
                {
                    return selectedObservable;
                }
            }
        }

        /// <summary>
        /// Testing exception with equality testing.
        /// </summary>
        class TestException : Exception
        {
            public TestException(string message)
                : base(message)
            {

            }

            public override bool Equals(object obj)
            {
                var other = (TestException)obj;

                return this.Message == other.Message;
            }
        }

        [Test]
        public void Completes_when_input_completes()
        {
            TestScheduler scheduler = new TestScheduler();

            var input = scheduler.CreateColdObservable(
                OnCompleted<int>(210)
                );

            Func<IEnumerable<int>, IObservable<int>> selector = items => scheduler.CreateHotObservable(OnCompleted<int>(1));

            var query = input.BufferWhileRunning(selector);

            var result = scheduler.Start(() => query);

            result.Messages.AssertEqual(
                OnCompleted<int>(210)
                );

            Assert.AreEqual(new Subscription(0, 210), input.Subscriptions.Single());
        }

        [Test]
        public void Selector_not_called_when_not_subscribed()
        {
            TestScheduler scheduler = new TestScheduler();

            var input = scheduler.CreateColdObservable(
                OnNext(210, 1),
                OnCompleted<int>(300)
                );

            List<SelectorCall> calls = new List<SelectorCall>();

            Func<IEnumerable<int>, IObservable<Items<int>>> selector = items =>
            {
                var selectedObservable = scheduler.CreateHotObservable(OnCompleted<Items<int>>(1));

                calls.Add(new SelectorCall(scheduler.Clock, items, selectedObservable));

                return selectedObservable;
            };

            var query = input.BufferWhileRunning(selector);

            scheduler.Start();

            CollectionAssert.IsEmpty(calls);
        }

        [Test]
        public void Empty_selected_observable_does_nothing()
        {
            TestScheduler scheduler = new TestScheduler();

            var input = scheduler.CreateColdObservable(
                OnNext(210, 1),
                OnCompleted<int>(300)
                );

            Func<IEnumerable<int>, IObservable<int>> selector = items => scheduler.CreateColdObservable(OnCompleted<int>(2));

            var query = input.BufferWhileRunning(selector);

            var result = scheduler.Start(() => query);

            result.Messages.AssertEqual(
                OnCompleted<int>(300)
                );
        }

        [Test]
        public void Unsubscribes_from_current_when_input_completes()
        {
            TestScheduler scheduler = new TestScheduler();

            var input = scheduler.CreateColdObservable(
                OnNext(210, 1),
                OnCompleted<int>(300)
                );

            List<SelectorCall> calls = new List<SelectorCall>();

            Func<IEnumerable<int>, IObservable<Items<int>>> selector = items =>
            {
                var selectedObservable = scheduler.CreateColdObservable(OnCompleted<Items<int>>(1000));

                calls.Add(new SelectorCall(scheduler.Clock, items, selectedObservable));

                return selectedObservable;
            };

            var query = input.BufferWhileRunning(selector);

            var result = scheduler.Start(() => query);

            Assert.AreEqual(new Subscription(210, 300), calls[0].SelectedObservable.Subscriptions.Single());
        }

        [Test]
        public void Returns_selected_observables_when_not_overlapping()
        {
            TestScheduler scheduler = new TestScheduler();

            var input = scheduler.CreateColdObservable(
                OnNext(210, 1),
                OnNext(220, 2),
                OnNext(230, 3),
                OnCompleted<int>(300)
                );

            Func<IEnumerable<int>, IObservable<Items<int>>> selector = items => scheduler.CreateColdObservable(OnNext(1, new Items<int>(items)), OnCompleted<Items<int>>(1));

            var query = input.BufferWhileRunning(selector);

            var result = scheduler.Start(() => query);

            result.Messages.AssertEqual(
                OnNext(211, new Items<int>(1)),
                OnNext(221, new Items<int>(2)),
                OnNext(231, new Items<int>(3)),
                OnCompleted<Items<int>>(300)
                );
        }

        [Test]
        public void Subscribes_and_unsubsribes_once_for_each_selected()
        {
            TestScheduler scheduler = new TestScheduler();

            var input = scheduler.CreateColdObservable(
                OnNext(210, 1),
                OnNext(220, 2),
                OnNext(230, 3),
                OnCompleted<int>(300)
                );

            List<SelectorCall> calls = new List<SelectorCall>();

            Func<IEnumerable<int>, IObservable<Items<int>>> selector = items =>
            {
                var selectedObservable = scheduler.CreateColdObservable(OnNext(1, new Items<int>(items)), OnCompleted<Items<int>>(1));

                calls.Add(new SelectorCall(scheduler.Clock, items, selectedObservable));

                return selectedObservable;
            };

            var query = input.BufferWhileRunning(selector);

            var result = scheduler.Start(() => query);

            Assert.AreEqual(new Subscription(210, 211), calls[0].SelectedObservable.Subscriptions.Single());
            Assert.AreEqual(new Subscription(220, 221), calls[1].SelectedObservable.Subscriptions.Single());
            Assert.AreEqual(new Subscription(230, 231), calls[2].SelectedObservable.Subscriptions.Single());
        }

        [Test]
        public void Selector_call_delayed_after_previous_observable_completes()
        {
            TestScheduler scheduler = new TestScheduler();

            var input = scheduler.CreateColdObservable(
                OnNext(210, 1),
                OnNext(213, 2),
                OnCompleted<int>(300)
                );

            List<SelectorCall> calls = new List<SelectorCall>();

            Func<IEnumerable<int>, IObservable<Items<int>>> selector = items =>
            {
                var selectedObservable = scheduler.CreateColdObservable(OnCompleted<Items<int>>(5));

                calls.Add(new SelectorCall(scheduler.Clock, items, selectedObservable));

                return selectedObservable;
            };

            var query = input.BufferWhileRunning(selector);

            var result = scheduler.Start(() => query);

            Assert.AreEqual(210, calls[0].Clock);
            CollectionAssert.AreEqual(new[] { 1 }, calls[0].Items);

            Assert.AreEqual(215, calls[1].Clock);
            CollectionAssert.AreEqual(new[] { 2 }, calls[1].Items);
        }

        [Test]
        public void Multiple_events_are_buffered_when_current_is_running()
        {
            TestScheduler scheduler = new TestScheduler();

            var input = scheduler.CreateColdObservable(
                OnNext(210, 1),
                OnNext(213, 2),
                OnNext(215, 3),
                OnNext(219, 4),
                OnCompleted<int>(300)
                );

            List<SelectorCall> calls = new List<SelectorCall>();

            Func<IEnumerable<int>, IObservable<Items<int>>> selector = items =>
            {
                var selectedObservable = scheduler.CreateColdObservable(OnCompleted<Items<int>>(10));

                calls.Add(new SelectorCall(scheduler.Clock, items, selectedObservable));

                return selectedObservable;
            };

            var query = input.BufferWhileRunning(selector);

            var result = scheduler.Start(() => query);

            Assert.AreEqual(210, calls[0].Clock);
            CollectionAssert.AreEqual(new[] { 1 }, calls[0].Items);

            Assert.AreEqual(220, calls[1].Clock);
            CollectionAssert.AreEqual(new[] { 2, 3, 4 }, calls[1].Items);
        }

        [Test]
        public void Returns_exception_when_one_happens_on_input()
        {
            TestScheduler scheduler = new TestScheduler();

            var input = scheduler.CreateColdObservable(
                OnError<int>(210, new TestException("input error"))
                );

            Func<IEnumerable<int>, IObservable<int>> selector = items => scheduler.CreateHotObservable(OnCompleted<int>(1));

            var query = input.BufferWhileRunning(selector);

            var result = scheduler.Start(() => query);

            result.Messages.AssertEqual(
                OnError<int>(210, new TestException("input error"))
                );
        }

        [Test]
        public void Unsubscribes_when_input_excepts()
        {
            TestScheduler scheduler = new TestScheduler();

            var input = scheduler.CreateColdObservable(
                OnError<int>(210, new TestException("input error"))
                );

            Func<IEnumerable<int>, IObservable<int>> selector = items => scheduler.CreateHotObservable(OnCompleted<int>(1));

            var query = input.BufferWhileRunning(selector);

            var result = scheduler.Start(() => query);

            Assert.AreEqual(new Subscription(0, 210), input.Subscriptions.Single());
        }

        [Test]
        public void Returns_exception_when_one_happens_on_selection()
        {
            TestScheduler scheduler = new TestScheduler();

            var input = scheduler.CreateColdObservable(
                    OnNext(210, 1),
                    OnCompleted<int>(300)
                );

            Func<IEnumerable<int>, IObservable<int>> selector = items => scheduler.CreateHotObservable(OnError<int>(1, new TestException("selection error")));

            var query = input.BufferWhileRunning(selector);

            var result = scheduler.Start(() => query);

            result.Messages.AssertEqual(
                OnError<int>(211, new TestException("selection error"))
                );
        }

        [Test]
        public void Unsubscribes_from_input_when_selection_exception()
        {
            TestScheduler scheduler = new TestScheduler();

            var input = scheduler.CreateColdObservable(
                    OnNext(210, 1),
                    OnCompleted<int>(300)
                );

            Func<IEnumerable<int>, IObservable<int>> selector = items => scheduler.CreateHotObservable(OnError<int>(1, new TestException("selection error")));

            var query = input.BufferWhileRunning(selector);

            var result = scheduler.Start(() => query);

            Assert.AreEqual(new Subscription(0, 211), input.Subscriptions.Single());
        }

        [Test]
        public void Unsubscribes_from_selection_when_selection_exception()
        {
            TestScheduler scheduler = new TestScheduler();

            var input = scheduler.CreateColdObservable(
                    OnNext(210, 1),
                    OnCompleted<int>(300)
                );

            List<SelectorCall> calls = new List<SelectorCall>();

            Func<IEnumerable<int>, IObservable<Items<int>>> selector = items =>
            {
                var selectedObservable = scheduler.CreateHotObservable(OnError<Items<int>>(1, new TestException("selection error")));

                calls.Add(new SelectorCall(scheduler.Clock, items, selectedObservable));

                return selectedObservable;
            };

            var query = input.BufferWhileRunning(selector);

            var result = scheduler.Start(() => query);

            Assert.AreEqual(new Subscription(210, 211), calls[0].SelectedObservable.Subscriptions.Single());
        }
    }
}
