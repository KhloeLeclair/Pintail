using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using NUnit.Framework;

namespace Nanoray.Pintail.Tests
{
    [TestFixture]
    public class EnumeratorTests
    {
        public static class Provider
        {
            public interface IThingTwo<TValue>
            {
                TValue? Value { get; }
                int Extra { get; }
            }

            public interface IThingOne<TValue> : IReadOnlyDictionary<string, TValue>
            {
                IReadOnlyDictionary<string, TValue> CalculatedValues { get; }
            }

            public sealed class ThingTwo<TValue> : IThingTwo<TValue>
            {
                public TValue? Value { get; }
                public int Extra => 42;

                public ThingTwo(TValue? value)
                {
                    this.Value = value;
                }
            }

            public sealed class ThingOne<TValue> : IThingOne<TValue>
            {
                private readonly Dictionary<string, TValue> Storage = new();

                public void Add(string key, TValue value) => this.Storage.Add(key, value);
                public IReadOnlyDictionary<string, TValue> CalculatedValues => this.Storage;
                public TValue this[string key] => this.Storage[key];
                public IEnumerable<string> Keys => this.Storage.Keys;
                public IEnumerable<TValue> Values => this.Storage.Values;
                public int Count => this.Storage.Count;
                public bool ContainsKey(string key) => this.Storage.ContainsKey(key);
                public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator() => this.Storage.GetEnumerator();
                public bool TryGetValue(string key, [MaybeNullWhen(false)] out TValue value) => this.Storage.TryGetValue(key, out value);
                IEnumerator IEnumerable.GetEnumerator() => this.Storage.GetEnumerator();
            }

            public sealed class Api
            {
                public IThingOne<IThingTwo<StringBuilder>> GetThing()
                {
                    var result = new ThingOne<IThingTwo<StringBuilder>>();
                    result.Add("test", new ThingTwo<StringBuilder>(new StringBuilder("value")));
                    return result;
                }
            }
        }

        public static class ExactConsumer
        {
            public interface IThingTwo<TValue>
            {
                TValue? Value { get; }
                int Extra { get; }
            }

            public interface IThingOne<TValue> : IReadOnlyDictionary<string, TValue>
            {
                IReadOnlyDictionary<string, TValue> CalculatedValues { get; }
            }

            public interface IApi
            {
                IThingOne<IThingTwo<StringBuilder>> GetThing();
            }
        }

        public static class SubsetConsumer
        {
            public interface IThingTwo<TValue>
            {
                TValue? Value { get; }
            }

            public interface IThingOne<TValue> : IReadOnlyDictionary<string, TValue>
            {
                IReadOnlyDictionary<string, TValue> CalculatedValues { get; }
            }

            public interface IApi
            {
                IThingOne<IThingTwo<StringBuilder>> GetThing();
            }
        }


        private ProxyManager<Nothing> CreateProxyManager(ProxyManagerConfiguration<Nothing>? configuration = null)
        {
            var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName($"Nanoray.Pintail.Proxies, Version={this.GetType().Assembly.GetName().Version}, Culture=neutral"), AssemblyBuilderAccess.Run);
            var moduleBuilder = assemblyBuilder.DefineDynamicModule($"Proxies");
            var manager = new ProxyManager<Nothing>(moduleBuilder, configuration);
            return manager;
        }

        private ProxyManager<Nothing> CreateProxyManager(ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            return this.CreateProxyManager(new ProxyManagerConfiguration<Nothing> {
                ProxyPrepareBehavior = prepareBehavior
            });
        }

        [Test]
        public void TestExactObtain([Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var manager = this.CreateProxyManager(prepareBehavior);
            var api = manager.ObtainProxy<ExactConsumer.IApi>(new Provider.Api());

            Assert.AreEqual(1, api.GetThing().Count);
        }

        [Test]
        public void TestExactKeysAndIndexer([Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var manager = this.CreateProxyManager(prepareBehavior);
            var api = manager.ObtainProxy<ExactConsumer.IApi>(new Provider.Api());
            var thing = api.GetThing();

            foreach (string key in thing.Keys)
                Assert.AreEqual("value", thing[key].Value!.ToString());
        }

        [Test]
        public void TestExactTryGetValue([Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var manager = this.CreateProxyManager(prepareBehavior);
            var api = manager.ObtainProxy<ExactConsumer.IApi>(new Provider.Api());
            var thing = api.GetThing();

            Assert.IsTrue(thing.TryGetValue("test", out var value));
            Assert.AreEqual("value", value!.Value!.ToString());
        }

        [Test]
        public void TestExactEnumerateDirectly([Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var manager = this.CreateProxyManager(prepareBehavior);
            var api = manager.ObtainProxy<ExactConsumer.IApi>(new Provider.Api());
            var thing = api.GetThing();
            int count = 0;

            foreach (var entry in thing)
            {
                Assert.AreEqual("test", entry.Key);
                Assert.AreEqual("value", entry.Value.Value!.ToString());
                count++;
            }

            Assert.AreEqual(1, count);
        }

        [Test]
        public void TestExactEnumerateNestedDictionary([Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var manager = this.CreateProxyManager(prepareBehavior);
            var api = manager.ObtainProxy<ExactConsumer.IApi>(new Provider.Api());
            var thing = api.GetThing();
            int count = 0;

            foreach (var entry in thing.CalculatedValues)
            {
                Assert.AreEqual("value", entry.Value.Value!.ToString());
                count++;
            }

            Assert.AreEqual(1, count);
        }

        [Test]
        public void TestSubsetObtain([Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var manager = this.CreateProxyManager(prepareBehavior);
            var api = manager.ObtainProxy<SubsetConsumer.IApi>(new Provider.Api());
            
            Assert.AreEqual(1, api.GetThing().Count);
        }

        [Test]
        public void TestSubsetKeysAndIndexer([Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var manager = this.CreateProxyManager(prepareBehavior);
            var api = manager.ObtainProxy<SubsetConsumer.IApi>(new Provider.Api());
            var thing = api.GetThing();

            foreach (string key in thing.Keys)
                Assert.AreEqual("value", thing[key].Value!.ToString());
        }

        [Test]
        public void TestSubsetTryGetValue([Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var manager = this.CreateProxyManager(prepareBehavior);
            var api = manager.ObtainProxy<SubsetConsumer.IApi>(new Provider.Api());
            var thing = api.GetThing();

            Assert.IsTrue(thing.TryGetValue("test", out var value));
            Assert.AreEqual("value", value!.Value!.ToString());
        }

        [Test]
        public void TestSubsetEnumerateDirectly([Values] ProxyManagerProxyPrepareBehavior prepareBehavior)
        {
            var manager = this.CreateProxyManager(prepareBehavior);
            var api = manager.ObtainProxy<SubsetConsumer.IApi>(new Provider.Api());
            var thing = api.GetThing();
            int count = 0;

            foreach (var entry in thing)
            {
                Assert.AreEqual("test", entry.Key);
                Assert.AreEqual("value", entry.Value.Value!.ToString());
                count++;
            }

            Assert.AreEqual(1, count);
        }

    }
}
