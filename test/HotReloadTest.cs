using System;
using System.Collections.Generic;
using System.Linq;
using Leopotam.Ecs;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Kk.LeoHotClassic
{
    public class HotReloadTest
    {
        [Serializable]
        public struct Comp1
        {
            public int value;
            public Object obj;
        }
        
        public struct Comp3
        {
            public int value;
        }

        [Test]
        public void TestScalarsAndUnityRefs()
        {
            Test(
                init: (systems, universe) => { },
                act: systems =>
                {
                    EcsWorld world = systems.World;
                    EcsEntity a = world.NewEntity();
                    a.Get<Comp1>().value = 42;
                    GameObject hello = new GameObject("hello");
                    a.Get<Comp1>().obj = hello;
                },
                verify: systems =>
                {
                    List<Comp1> c1s = new List<Comp1>();
                    EcsFilter filter = systems.World.GetFilter(typeof(EcsFilter<Comp1>));
                    foreach (int i in filter) 
                    { 
                        Comp1 comp1 = filter.GetEntity(i).Get<Comp1>();
                        c1s.Add(comp1);
                    }

                    Assert.AreEqual(1, c1s.Count);
                    Assert.AreEqual(42, c1s.Single().value);
                    Assert.AreEqual("hello", c1s.Single().obj.name);
                }
            );
        }

        [Test]
        public void TestScalarsAndUnityRefsNotSerializable()
        {
            Test(
                init: (systems, universe) => { },
                act: systems =>
                {
                    EcsWorld world = systems.World;
                    EcsEntity a = world.NewEntity();
                    a.Get<Comp3>().value = 42;
                },
                verify: systems =>
                {
                    List<Comp3> c1s = new List<Comp3>();
                    EcsFilter filter = systems.World.GetFilter(typeof(EcsFilter<Comp3>));
                    foreach (int i in filter)
                    {
                        Comp3 comp1 = filter.GetEntity(i).Get<Comp3>();
                        c1s.Add(comp1);
                    }

                    Assert.AreEqual(0, c1s.Count);
                    LogAssert.Expect(LogType.Error, $"component is not serializable: {typeof(Comp3)}");
                }
            );
        }

        [Serializable]
        public struct Comp2
        {
            public EcsEntity entity;
            public int num;
        }

        [Test]
        public void TestPackedEntity()
        {
            Test(
                init: (systems, universe) => { },
                act: systems =>
                {
                    EcsEntity a = systems.World.NewEntity();
                    a.Get<Comp1>().value = 42;

                    EcsEntity b = systems.World.NewEntity();
                    b.Get<Comp2>();
                    b.Get<Comp2>().num = 17;
                    b.Get<Comp2>().entity = a;
                },
                verify: systems =>
                {
                    EcsEntity b = SingleEntity(systems.World.GetFilter(typeof(EcsFilter<Comp2>)));
                    Assert.AreEqual(17, b.Get<Comp2>().num);
                    EcsEntity a = b.Get<Comp2>().entity;
                    Assert.True(a.IsAlive());
                    Assert.AreEqual(42, a.Get<Comp1>().value);
                }
            );
        }

        [Test]
        public void TestIncomingLink()
        {
            EcsEntityLink link = new GameObject().AddComponent<EcsEntityLink>();
            try
            {
                Test(
                    (systems, universe) => { },
                    systems =>
                    {
                        EcsEntity a = systems.World.NewEntity();
                        a.Get<Comp1>().value = 42;
                        link.entity = a;
                    },
                    systems =>
                    {
                        Assert.True(link.entity.IsAlive());
                        Assert.AreEqual(42, link.entity.Get<Comp1>().value);
                    }
                );
            }
            finally
            {
                Object.DestroyImmediate(link.gameObject);
            }
        }

        private static EcsEntity SingleEntity(EcsFilter filter)
        {
            foreach (int i in filter)
            {
                if (filter.GetEntitiesCount() > 1)
                {
                    throw new Exception("WTF");
                }

                return filter.GetEntity(i);
            }

            throw new Exception("WTF");
        }

        private static void Test(Action<EcsSystems, SerializableEcsUniverse> init, Action<EcsSystems> act, Action<EcsSystems> verify)
        {
            string s;
            {
                EcsSystems systems = new EcsSystems(new EcsWorld());
                SerializableEcsUniverse universe = new SerializableEcsUniverse();
                init(systems, universe);

                act(systems);
                universe.PackState(systems);
                s = JsonUtility.ToJson(universe);
            }

            {
                EcsSystems systems = new EcsSystems(new EcsWorld());
                SerializableEcsUniverse universe = JsonUtility.FromJson<SerializableEcsUniverse>(s);
                init(systems, universe);

                universe.UnpackState(systems);

                verify(systems);
            }
        }
    }
}