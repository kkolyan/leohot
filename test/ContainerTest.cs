using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Kk.LeoHotClassic
{
    public class ContainerTest
    {
        [Serializable] private struct SI { public int value; }
        [Serializable] private struct SF { public float value; }
        [Serializable] private struct SB { public bool value; }
        [Serializable] private struct SS { public string value; }
        [Serializable] private struct SV { public Vector3 value; }
        [Serializable] private struct SU { public Object value; }
        [Serializable] private struct SD { [SerializeReference] public object value; }
        [Serializable] private struct SR { public SI value; }
        [Serializable] private struct SA { public int[] value; }
        [Serializable] private struct SL { public List<int> value; }
        [Serializable] public struct NSP { public NS value; }
        public struct NS { public int value; }

        [Test]
        public void TestInt()
        {
            int a = 0;
            Test(
                container => { },
                container => { a = container.Pack(new SI { value = 42 }); },
                objects =>
                {
                    object o = objects[a];
                    Assert.AreEqual(typeof(SI), o.GetType());
                    Assert.AreEqual(42, ((SI)o).value);
                }
            );
        }

        [Test]
        public void TestFloat()
        {
            int a = 0;
            Test(
                container => { },
                container => { a = container.Pack(new SF { value = 42f }); },
                objects =>
                {
                    object o = objects[a];
                    Assert.AreEqual(typeof(SF), o.GetType());
                    Assert.AreEqual(42f, ((SF)o).value);
                }
            );
        }

        [Test]
        public void TestBool()
        {
            int a = 0;
            Test(
                container => { },
                container => { a = container.Pack(new SB { value = true }); },
                objects =>
                {
                    object o = objects[a];
                    Assert.AreEqual(typeof(SB), o.GetType());
                    Assert.AreEqual(true, ((SB)o).value);
                }
            );
        }

        [Test]
        public void TestString()
        {
            int a = 0;
            Test(
                container => { },
                container => { a = container.Pack(new SS { value = "42" }); },
                objects =>
                {
                    object o = objects[a];
                    Assert.AreEqual(typeof(SS), o.GetType());
                    Assert.AreEqual("42", ((SS)o).value);
                }
            );
        }

        [Test]
        public void TestVector()
        {
            int a = 0;
            Test(
                container => { },
                container => { a = container.Pack(new SV { value = new Vector3(42, 17, -1) }); },
                objects =>
                {
                    object o = objects[a];
                    Assert.AreEqual(typeof(SV), o.GetType());
                    Assert.AreEqual(new Vector3(42, 17, -1), ((SV)o).value);
                }
            );
        }

        [Test]
        public void TestUnityObject()
        {
            int a = 0;
            Test(
                container => { },
                container => { a = container.Pack(new SU { value = new GameObject("42") }); },
                objects =>
                {
                    object o = objects[a];
                    Assert.AreEqual(typeof(SU), o.GetType());
                    Assert.AreEqual("42", ((SU)o).value.name);
                }
            );
        }

        [Test]
        public void TestDynamic()
        {
            int a = 0;
            Test(
                container => { },
                container => { a = container.Pack(new SD { value = new SI { value = 42 } }); },
                objects =>
                {
                    object o = objects[a];
                    Assert.AreEqual(typeof(SD), o.GetType());
                    Assert.AreEqual(42, ((SI)((SD)o).value).value);
                }
            );
        }

        [Test]
        public void TestStatic()
        {
            int a = 0;
            Test(
                container => { },
                container => { a = container.Pack(new SR { value = new SI { value = 42 } }); },
                objects =>
                {
                    object o = objects[a];
                    Assert.AreEqual(typeof(SR), o.GetType());
                    Assert.AreEqual(42, ((SR)o).value.value);
                }
            );
        }

        [Test]
        public void TestArray()
        {
            int a = 0;
            Test(
                container => { },
                container => { a = container.Pack(new SA { value = new[] { 42, 17 } }); },
                objects =>
                {
                    object o = objects[a];
                    Assert.AreEqual(typeof(SA), o.GetType());
                    Assert.AreEqual(2, ((SA)o).value.Length);
                    Assert.AreEqual(42, ((SA)o).value[0]);
                    Assert.AreEqual(17, ((SA)o).value[1]);
                }
            );
        }

        [Test]
        public void TestList()
        {
            int a = 0;
            Test(
                container => { },
                container => { a = container.Pack(new SL { value = new List<int> { 42, 17 } }); },
                objects =>
                {
                    object o = objects[a];
                    Assert.AreEqual(typeof(SL), o.GetType());
                    Assert.AreEqual(2, ((SL)o).value.Count);
                    Assert.AreEqual(42, ((SL)o).value[0]);
                    Assert.AreEqual(17, ((SL)o).value[1]);
                }
            );
        }

        [Test]
        public void TestNonSerialized()
        {
            int a = 0;
            Test(
                container => { },
                container => { a = container.Pack(new NSP { value = new NS { value = 42 } }); },
                objects =>
                {
                    object o = objects[a];
                    Assert.AreEqual(typeof(NSP), o.GetType());
                    Assert.AreEqual(0, ((NSP)o).value.value);
                }
            );
        }

        [Test]
        public void TestNonSerializedButCustomized()
        {
            int a = 0;
            Test(
                container =>
                {
                    container.AddConverter<NS, int>(
                        nsp => nsp.value,
                        i => new NS { value = i }
                    );
                },
                container => { a = container.Pack(new NSP { value = new NS { value = 42 } }); },
                objects =>
                {
                    object o = objects[a];
                    Assert.AreEqual(typeof(NSP), o.GetType());
                    Assert.AreEqual(42, ((NSP)o).value.value);
                }
            );
        }


        private static void Test(
            Action<SerializableObjectContainer> init,
            Action<SerializableObjectContainer> act,
            Action<IDictionary<int, object>> verify
        )
        {
            string json;
            {
                SerializableObjectContainer container = new SerializableObjectContainer();
                init(container);
                // just garbage to make actual test indices more different 
                for (int i = 0; i < 3; i++) container.Pack(new SI { value = i });
                for (int i = 0; i < 4; i++) container.Pack(new SF { value = i });
                for (int i = 0; i < 5; i++) container.Pack(new SS { value = i.ToString() });

                act(container);
                json = JsonUtility.ToJson(container);
            }
            {
                SerializableObjectContainer container = JsonUtility.FromJson<SerializableObjectContainer>(json);
                init(container);

                Dictionary<int, object> unpacked = container.Unpack();

                verify(unpacked);
            }
        }
    }
}