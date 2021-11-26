using System;
using System.Collections.Generic;
using System.Linq;
using Kk.LeoHot;
using Leopotam.EcsLite;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Kk.LeoHot
{
    public class HotReloadTest
    {
        [Serializable]
        public struct Comp1
        {
            public int value;
            public Object obj;
        }

        [Test]
        public void TestScalarsAndUnityRefs()
        {
            Test(
                init: (systems, universe) => { },
                act: systems =>
                {
                    EcsWorld world = systems.GetWorld();
                    int a = world.NewEntity();
                    world.GetPool<Comp1>().Add(a);
                    world.GetPool<Comp1>().Get(a).value = 42;
                    GameObject hello = new GameObject("hello");
                    world.GetPool<Comp1>().Get(a).obj = hello;
                },
                verify: systems =>
                {
                    List<Comp1> c1s = new List<Comp1>();
                    foreach (int i in systems.GetWorld().Filter<Comp1>().End())
                    {
                        Comp1 comp1 = systems.GetWorld().GetPool<Comp1>().Get(i);
                        c1s.Add(comp1);
                    }

                    Assert.AreEqual(1, c1s.Count);
                    Assert.AreEqual(42, c1s.Single().value);
                    Assert.AreEqual("hello", c1s.Single().obj.name);
                }
            );
        }

        [Serializable]
        public struct Comp2
        {
            public EcsPackedEntityWithWorld entity;
            public int num;
        }

        [Test]
        public void TestPackedEntity()
        {
            Test(
                init: (systems, universe) => { },
                act: systems =>
                {
                    int a = systems.GetWorld().NewEntity();
                    systems.GetWorld().GetPool<Comp1>().Add(a).value = 42;

                    int b = systems.GetWorld().NewEntity();
                    systems.GetWorld().GetPool<Comp2>().Add(b);
                    systems.GetWorld().GetPool<Comp2>().Get(b).num = 17;
                    systems.GetWorld().GetPool<Comp2>().Get(b).entity = systems.GetWorld().PackEntityWithWorld(a);
                },
                verify: systems =>
                {
                    int b = SingleEntity(systems.GetWorld().Filter<Comp2>().End());
                    Assert.AreEqual(17, systems.GetWorld().GetPool<Comp2>().Get(b).num);
                    bool success = systems.GetWorld().GetPool<Comp2>().Get(b).entity.Unpack(out var world, out int a);
                    Assert.True(success);
                    Assert.AreEqual(42, systems.GetWorld().GetPool<Comp1>().Get(a).value);
                }
            );
        }

        [Test]
        public void TestIncomingLink()
        {
            EcsEntityLink link = new GameObject().AddComponent<EcsEntityLink>();
            Test(
                (systems, universe) => { },
                systems =>
                {
                    int a = systems.GetWorld().NewEntity();
                    systems.GetWorld().GetPool<Comp1>().Add(a).value = 42;
                    link.entity = systems.GetWorld().PackEntityWithWorld(a);
                },
                systems =>
                {
                    Assert.True(link.entity.Unpack(out var world, out var entity));
                    Assert.AreEqual(42, world.GetPool<Comp1>().Get(entity).value);
                }
            );
            Object.DestroyImmediate(link.gameObject);
        }

        private static int SingleEntity(EcsFilter filter)
        {
            foreach (int i in filter)
            {
                if (filter.GetEntitiesCount() > 1)
                {
                    throw new Exception("WTF");
                }

                return i;
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