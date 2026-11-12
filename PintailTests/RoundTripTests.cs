using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Nanoray.Pintail.Tests
{
    [TestFixture]
    public class RoundTripTests
    {
        public static class Provider
        {
            public interface IThing
            {
                string Name { get; }
                int Extra { get; }
            }

            public sealed class Thing : IThing
            {
                public string Name => "thing";
                public int Extra => 42;
            }

            public interface IHook
            {
                void OnThing(IThing thing);
            }

            public interface IPicker
            {
                IThing Pick(IThing thing);
            }

            public sealed class Api
            {
                private readonly Thing Thing = new();

                public IThing Create() => this.Thing;
                public string Use(IThing thing) => $"{thing.Name}{thing.Extra}";
                public bool TryGet(out IThing thing) { thing = this.Thing; return true; }
                public void On(Action<IThing> callback) => callback(this.Thing);
                public void Register(IHook hook) => hook.OnThing(this.Thing);
                public bool PickIsSame(IPicker picker) => ReferenceEquals(picker.Pick(this.Thing), this.Thing);
                public IList<IThing> GetList() => new List<IThing> { this.Thing };
                public bool MapIsSame(Func<IThing, IThing> mapper) => ReferenceEquals(mapper(this.Thing), this.Thing);

                public void Swap(ref IThing thing) => thing = this.Thing;

                private int HandlerCalls;
                public Action<IThing> GetHandler() => _ => this.HandlerCalls++;
                public int CallHandler(Action<IThing> handler) { handler(this.Thing); return this.HandlerCalls; }

                public event Action<IThing>? Changed;
                public void Fire() => this.Changed?.Invoke(this.Thing);
            }
        }

        public static class Consumer
        {
            public interface IThing
            {
                string Name { get; }
            }

            public sealed class OwnThing : IThing
            {
                public string Name => "own";
            }

            public interface IHook
            {
                void OnThing(IThing thing);
            }

            public sealed class Hook : IHook
            {
                public string? Seen;
                public void OnThing(IThing thing) => this.Seen = thing.Name;
            }

            public interface IPicker
            {
                IThing Pick(IThing thing);
            }

            public sealed class Picker : IPicker
            {
                public IThing Pick(IThing thing) => thing;
            }

            public sealed class OwnPicker : IPicker
            {
                public IThing Pick(IThing thing) => new OwnThing();
            }

            public interface IBiggerThing
            {
                string Name { get; }
                int Extra { get; }
                int NonExistent { get; }
            }

            public interface IBiggerHook
            {
                void OnThing(IBiggerThing thing);
            }

            public sealed class BiggerHook : IBiggerHook
            {
                public void OnThing(IBiggerThing thing) { }
            }

            public interface IUnrelated
            {
                void Foo();
            }

            public interface IUnrelatedApi
            {
                string Use(IUnrelated thing);
            }

            public interface IRoundTripApi
            {
                IThing Create();
                string Use(IThing thing);
            }

            public interface IOutApi
            {
                bool TryGet(out IThing thing);
            }

            public interface IBiggerOutApi
            {
                bool TryGet(out IBiggerThing thing);
            }

            public interface IBiggerCallbackApi
            {
                void On(Action<IBiggerThing> callback);
            }

            public interface ICallbackApi
            {
                void On(Action<IThing> callback);
            }

            public interface IMapperApi
            {
                bool MapIsSame(Func<IThing, IThing> mapper);
            }

            public interface IHandlerApi
            {
                Action<IThing> GetHandler();
                int CallHandler(Action<IThing> handler);
            }

            public interface IHookApi
            {
                void Register(IHook hook);
            }

            public interface IPickerApi
            {
                bool PickIsSame(IPicker picker);
            }

            public interface IListApi
            {
                IThing Create();
                IList<IThing> GetList();
            }

            public interface ISwapApi
            {
                void Swap(ref IThing thing);
            }

            public interface IBiggerSwapApi
            {
                void Swap(ref IBiggerThing thing);
            }

            public interface IEventApi
            {
                event Action<IThing>? Changed;
                void Fire();
            }

            public interface IBiggerHookApi
            {
                void Register(IBiggerHook hook);
            }

            public interface IBiggerReturnApi
            {
                IBiggerThing Create();
            }
        }


        private ProxyManager<Nothing> CreateProxyManager(ProxyManagerConfiguration<Nothing>? configuration = null)
        {
            var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName($"Nanoray.Pintail.Proxies, Version={this.GetType().Assembly.GetName().Version}, Culture=neutral"), AssemblyBuilderAccess.Run);
            var moduleBuilder = assemblyBuilder.DefineDynamicModule($"Proxies");
            var manager = new ProxyManager<Nothing>(moduleBuilder, configuration);
            return manager;
        }

        private ProxyManager<Nothing> CreateProxyManager(ProxyManagerProxyPrepareBehavior prepareBehavior, ProxyManagerInputMappingBehavior inputMappingBehavior = ProxyManagerInputMappingBehavior.AllowRoundTrip)
        {
            return this.CreateProxyManager(new ProxyManagerConfiguration<Nothing>
            {
                InputMappingBehavior = inputMappingBehavior,
                ProxyPrepareBehavior = prepareBehavior
            });
        }

        private T ObtainApiProxy<T>(ProxyManagerProxyPrepareBehavior prepareBehavior, ProxyManagerInputMappingBehavior inputMappingBehavior = ProxyManagerInputMappingBehavior.AllowRoundTrip) where T : class
        {
            var manager = this.CreateProxyManager(prepareBehavior, inputMappingBehavior);
            return manager.ObtainProxy<T>(new Provider.Api());
        }

        [Test]
        public void TestStrictStillThrowsOnCreation([Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var manager = this.CreateProxyManager(prepareBehavior, ProxyManagerInputMappingBehavior.Strict);
            Assert.Throws<ArgumentException>(() => manager.ObtainProxy<Consumer.IRoundTripApi>(new Provider.Api()));
            Assert.Throws<ArgumentException>(() => manager.ObtainProxy<Consumer.IListApi>(new Provider.Api()));
            Assert.Throws<ArgumentException>(() => manager.ObtainProxy<Consumer.IPickerApi>(new Provider.Api()));
            Assert.Throws<ArgumentException>(() => manager.ObtainProxy<Consumer.ISwapApi>(new Provider.Api()));
        }

        [Test]
        public void TestRoundTrip([Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var api = this.ObtainApiProxy<Consumer.IRoundTripApi>(prepareBehavior);
            Assert.AreEqual("thing42", api.Use(api.Create()));
        }

        [Test]
        public void TestPassingOwnIncompleteImplementationThrowsOnCall([Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var api = this.ObtainApiProxy<Consumer.IRoundTripApi>(prepareBehavior);
            Assert.Throws<ArgumentException>(() => api.Use(new Consumer.OwnThing()));
        }

        [Test]
        public void TestOutParameter([Values] ProxyManagerInputMappingBehavior inputMappingBehavior, [Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var api = this.ObtainApiProxy<Consumer.IOutApi>(prepareBehavior, inputMappingBehavior);
            Assert.IsTrue(api.TryGet(out var thing));
            Assert.AreEqual("thing", thing.Name);
        }

        [Test]
        public void TestCallback([Values] ProxyManagerInputMappingBehavior inputMappingBehavior, [Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var api = this.ObtainApiProxy<Consumer.ICallbackApi>(prepareBehavior, inputMappingBehavior);
            string? seen = null;
            api.On(thing => seen = thing.Name);
            Assert.AreEqual("thing", seen);
        }

        [Test]
        public void TestEvent([Values] ProxyManagerInputMappingBehavior inputMappingBehavior, [Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var api = this.ObtainApiProxy<Consumer.IEventApi>(prepareBehavior, inputMappingBehavior);
            string? seen = null;
            api.Changed += thing => seen = thing.Name;
            api.Fire();
            Assert.AreEqual("thing", seen);
        }

        [Test]
        public void TestHook([Values] ProxyManagerInputMappingBehavior inputMappingBehavior, [Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var api = this.ObtainApiProxy<Consumer.IHookApi>(prepareBehavior, inputMappingBehavior);
            var hook = new Consumer.Hook();
            api.Register(hook);
            Assert.AreEqual("thing", hook.Seen);
        }

        [Test]
        public void TestHookReturningRoundTrippedValue([Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var api = this.ObtainApiProxy<Consumer.IPickerApi>(prepareBehavior);
            Assert.IsTrue(api.PickIsSame(new Consumer.Picker()));
        }

        [Test]
        public void TestHookInvalidReturnType([Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var api = this.ObtainApiProxy<Consumer.IPickerApi>(prepareBehavior);
            Assert.Throws<ArgumentException>(() =>
            {
                api.PickIsSame(new Consumer.OwnPicker());
            });
        }

        [Test]
        public void TestList([Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var api = this.ObtainApiProxy<Consumer.IListApi>(prepareBehavior);
            var list = api.GetList();
            Assert.AreEqual("thing", list[0].Name);
            list.Add(api.Create());
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual("thing", list[1].Name);
        }

        [Test]
        public void TestListEnumeration([Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var api = this.ObtainApiProxy<Consumer.IListApi>(prepareBehavior);
            var list = api.GetList();

            int count = 0;
            foreach (var thing in list)
            {
                Assert.AreEqual("thing", thing.Name);
                count++;
            }
            Assert.AreEqual(1, count);

            var created = api.Create();
            Assert.IsTrue(list.Contains(created));
            Assert.AreEqual(0, list.IndexOf(created));

            list.Add(created);
            count = 0;
            foreach (var thing in list)
            {
                Assert.AreEqual("thing", thing.Name);
                count++;
            }
            Assert.AreEqual(2, count);

            using var enumerator = list.GetEnumerator();
            Assert.IsTrue(enumerator.MoveNext());
            Assert.AreEqual("thing", enumerator.Current.Name);
            Assert.IsTrue(enumerator.MoveNext());
            Assert.AreEqual("thing", enumerator.Current.Name);
            Assert.IsFalse(enumerator.MoveNext());

            Assert.IsTrue(list.Remove(created));
            Assert.AreEqual(1, list.Count);
        }

        [Test]
        public void TestListAddInvalidValue([Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var api = this.ObtainApiProxy<Consumer.IListApi>(prepareBehavior);
            var list = api.GetList();
            var thing = new Consumer.OwnThing();

            Assert.Throws<ArgumentException>(() =>
            {
                list.Add(thing);
            });
        }

        [Test]
        public void TestHookExpectingMoreThanProvidedThrowsOnRegistration([Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            Assert.Throws<ArgumentException>(() =>
            {
                var api = this.ObtainApiProxy<Consumer.IBiggerHookApi>(prepareBehavior);
                api.Register(new Consumer.BiggerHook());
            });
        }

        [Test]
        public void TestUnrelatedParameterType([Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            Assert.Throws<ArgumentException>(() =>
            {
                var api = this.ObtainApiProxy<Consumer.IUnrelatedApi>(prepareBehavior);
            });
        }

        [Test]
        public void TestExtendedOutParameterType([Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            Assert.Throws<ArgumentException>(() =>
            {
                var api = this.ObtainApiProxy<Consumer.IBiggerOutApi>(prepareBehavior);
            });
        }

        [Test]
        public void TestRefSubsetParameter([Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var api = this.ObtainApiProxy<Consumer.ISwapApi>(prepareBehavior);
            Consumer.IThing thing = null!;
            api.Swap(ref thing);
            Assert.AreEqual("thing", thing.Name);
        }

        [Test]
        public void TestExtendedRefParameter([Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            Assert.Throws<ArgumentException>(() =>
            {
                var api = this.ObtainApiProxy<Consumer.IBiggerSwapApi>(prepareBehavior);
            });
        }

        [Test]
        public void TestCallbackExpectingMoreThanProvidedThrowsOnCreation([Values] ProxyManagerProxyPrepareBehavior prepareBehavior, [Values] ProxyManagerInputMappingBehavior inputMappingBehavior)
        {
            Assert.Throws<ArgumentException>(() => this.ObtainApiProxy<Consumer.IBiggerCallbackApi>(prepareBehavior, inputMappingBehavior));
        }

        [Test]
        public void TestCallbackReturningRoundTrippedValue([Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var api = this.ObtainApiProxy<Consumer.IMapperApi>(prepareBehavior);
            Assert.IsTrue(api.MapIsSame(thing => thing));
        }

        [Test]
        public void TestPassingBackDelegate([Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var api = this.ObtainApiProxy<Consumer.IHandlerApi>(prepareBehavior);
            Assert.AreEqual(1, api.CallHandler(api.GetHandler()));
        }

        [Test]
        public void TestReturnValuesAreStillStrict([Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var manager = this.CreateProxyManager(prepareBehavior);
            Assert.Throws<ArgumentException>(() => manager.ObtainProxy<Consumer.IBiggerReturnApi>(new Provider.Api())!.Create());
        }
    }
}
