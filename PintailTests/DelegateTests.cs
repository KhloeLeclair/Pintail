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
    public class DelegateTests
    {
        public static class Provider
        {
            public interface IThing
            {
                string Name { get; }
                int Absorbed { get; }
                void Absorb(IThing other);
            }

            public sealed class Thing : IThing
            {
                public string Name => "thing";
                public int Absorbed { get; private set; }
                public void Absorb(IThing other) => this.Absorbed++;
            }

            public sealed class Api
            {
                private readonly Thing Thing = new();
                private readonly Action<IThing> Handler;
                private readonly Action<IThing> OtherHandler;
                private readonly List<Action<IThing>> Registered = new();

                public int HandlerCallCount { get; private set; }
                public int OtherHandlerCallCount { get; private set; }

                public Api()
                {
                    this.Handler = _ => this.HandlerCallCount++;
                    this.OtherHandler = _ => this.OtherHandlerCallCount++;
                    this.Registered.Add(this.Handler);
                }

                public IThing Create() => this.Thing;
                public Action<IThing> GetHandler() => this.Handler;
                public Action<IThing> GetOtherHandler() => this.OtherHandler;
                public bool IsHandler(Action<IThing> handler) => ReferenceEquals(handler, this.Handler);
                public bool RemoveHandler(Action<IThing> handler) => this.Registered.Remove(handler);
                public void CallHandler(Action<IThing> handler) => handler(this.Thing);

                public bool AreAllHandlers(Action<IThing>[] handlers)
                {
                    foreach (var handler in handlers)
                        if (!ReferenceEquals(handler, this.Handler))
                            return false;
                    return true;
                }

                public event Action<IThing>? Changed;
                public void Fire() => this.Changed?.Invoke(this.Thing);
            }
        }

        public static class Consumer
        {
            public interface IThing
            {
                string Name { get; }
                int Absorbed { get; }
                void Absorb(IThing other);
            }

            public interface IApi
            {
                int HandlerCallCount { get; }
                int OtherHandlerCallCount { get; }

                IThing Create();
                Action<IThing> GetHandler();
                Action<IThing> GetOtherHandler();
                bool IsHandler(Action<IThing> handler);
                bool RemoveHandler(Action<IThing> handler);
                void CallHandler(Action<IThing> handler);
                bool AreAllHandlers(Action<IThing>[] handlers);

                event Action<IThing>? Changed;
                void Fire();
            }
        }

        public static class ThirdParty
        {
            public interface IThing
            {
                string Name { get; }
                int Absorbed { get; }
                void Absorb(IThing other);
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
        public void TestPassedBackDelegateIsTheOriginal([Values] ProxyManagerInputMappingBehavior inputMappingBehavior, [Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var api = this.ObtainApiProxy<Consumer.IApi>(prepareBehavior, inputMappingBehavior);
            Assert.IsTrue(api.IsHandler(api.GetHandler()));
            Assert.IsFalse(api.IsHandler(api.GetOtherHandler()));
        }

        [Test]
        public void TestPassedBackDelegateCanBeRemoved([Values] ProxyManagerInputMappingBehavior inputMappingBehavior, [Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var api = this.ObtainApiProxy<Consumer.IApi>(prepareBehavior, inputMappingBehavior);
            Assert.IsFalse(api.RemoveHandler(api.GetOtherHandler()));
            Assert.IsTrue(api.RemoveHandler(api.GetHandler()));
            Assert.IsFalse(api.RemoveHandler(api.GetHandler()));
        }

        [Test]
        public void TestPassedBackDelegatesInArrayAreTheOriginals([Values] ProxyManagerInputMappingBehavior inputMappingBehavior, [Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var api = this.ObtainApiProxy<Consumer.IApi>(prepareBehavior, inputMappingBehavior);
            Assert.IsTrue(api.AreAllHandlers(new[] { api.GetHandler(), api.GetHandler() }));
            Assert.IsFalse(api.AreAllHandlers(new[] { api.GetHandler(), api.GetOtherHandler() }));
        }

        [Test]
        public void TestObtainingProxyOfDelegateProxyUnproxies([Values] ProxyManagerInputMappingBehavior inputMappingBehavior, [Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var manager = this.CreateProxyManager(prepareBehavior, inputMappingBehavior);
            Action<Provider.IThing> original = _ => { };

            var proxy = manager.ObtainProxy<Action<Consumer.IThing>>(original)!;
            Assert.AreNotSame(original, proxy);
            Assert.AreSame(original, manager.ObtainProxy<Action<Provider.IThing>>(proxy));

            Assert.IsTrue(manager.TryProxy<Action<Provider.IThing>>(proxy, out var unproxied));
            Assert.AreSame(original, unproxied);
        }

        [Test]
        public void TestPassedBackDelegateStillWorks([Values] ProxyManagerInputMappingBehavior inputMappingBehavior, [Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var api = this.ObtainApiProxy<Consumer.IApi>(prepareBehavior, inputMappingBehavior);

            api.CallHandler(api.GetHandler());
            Assert.AreEqual(1, api.HandlerCallCount);
            Assert.AreEqual(0, api.OtherHandlerCallCount);
        }

        [Test]
        public void TestDelegateProxiedThroughThirdPartyStillWorks([Values] ProxyManagerInputMappingBehavior inputMappingBehavior, [Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var manager = this.CreateProxyManager(prepareBehavior, inputMappingBehavior);
            int calls = 0;
            Action<Provider.IThing> original = _ => calls++;

            var consumerProxy = manager.ObtainProxy<Action<Consumer.IThing>>(original)!;
            var thirdPartyProxy = manager.ObtainProxy<Action<ThirdParty.IThing>>(consumerProxy)!;
            thirdPartyProxy(manager.ObtainProxy<ThirdParty.IThing>(new Provider.Thing())!);
            Assert.AreEqual(1, calls);
            Assert.AreSame(original, manager.ObtainProxy<Action<Provider.IThing>>(thirdPartyProxy));
        }

        [Test]
        public void TestOwnDelegateIsNotUnproxied([Values] ProxyManagerInputMappingBehavior inputMappingBehavior, [Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var api = this.ObtainApiProxy<Consumer.IApi>(prepareBehavior, inputMappingBehavior);
            int calls = 0;
            Action<Consumer.IThing> own = _ => calls++;

            Assert.IsFalse(api.IsHandler(own));
            api.CallHandler(own);
            Assert.AreEqual(1, calls);
            Assert.AreEqual(0, api.HandlerCallCount);
        }

        [Test]
        public void TestCombinedDelegateOfProxyAndOwnIsNotUnproxied([Values] ProxyManagerInputMappingBehavior inputMappingBehavior, [Values] ProxyManagerProxyPrepareBehavior prepareBehavior, [Values] bool proxyLast)
        {
            var api = this.ObtainApiProxy<Consumer.IApi>(prepareBehavior, inputMappingBehavior);
            int calls = 0;
            Action<Consumer.IThing> own = _ => calls++;
            var combined = proxyLast ? own + api.GetHandler() : api.GetHandler() + own;

            // unproxying would lose the other delegate
            Assert.IsFalse(api.IsHandler(combined));
            api.CallHandler(combined);
            Assert.AreEqual(1, calls);
            Assert.AreEqual(1, api.HandlerCallCount);
        }

        [Test]
        public void TestCombinedDelegateOfTwoProxiesIsNotUnproxied([Values] ProxyManagerInputMappingBehavior inputMappingBehavior, [Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var api = this.ObtainApiProxy<Consumer.IApi>(prepareBehavior, inputMappingBehavior);
            var combined = api.GetHandler() + api.GetOtherHandler();

            Assert.IsFalse(api.IsHandler(combined));
            api.CallHandler(combined);
            Assert.AreEqual(1, api.HandlerCallCount);
            Assert.AreEqual(1, api.OtherHandlerCallCount);
        }

        [Test]
        public void TestDelegateOfProxiedInterfaceMethodIsNotUnproxied([Values] ProxyManagerInputMappingBehavior inputMappingBehavior, [Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var api = this.ObtainApiProxy<Consumer.IApi>(prepareBehavior, inputMappingBehavior);
            var thing = api.Create();

            // the target of this delegate is a proxy object, but of an interface - not of a delegate
            Action<Consumer.IThing> methodGroup = thing.Absorb;
            Assert.IsFalse(api.IsHandler(methodGroup));
            api.CallHandler(methodGroup);
            Assert.AreEqual(1, thing.Absorbed);
            Assert.AreEqual(0, api.HandlerCallCount);
        }

        [Test]
        public void TestOwnEventHandlerCanStillBeRemoved([Values] ProxyManagerInputMappingBehavior inputMappingBehavior, [Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var api = this.ObtainApiProxy<Consumer.IApi>(prepareBehavior, inputMappingBehavior);
            int calls = 0;
            Action<Consumer.IThing> own = _ => calls++;

            api.Changed += own;
            api.Fire();
            Assert.AreEqual(1, calls);

            api.Changed -= own;
            api.Fire();
            Assert.AreEqual(1, calls);
        }

    }
}
