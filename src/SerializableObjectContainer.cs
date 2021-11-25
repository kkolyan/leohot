using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Kk.LeoHot
{
    [Serializable]
    public class SerializableObjectContainer
    {
        [SerializeField] private List<int> roots = new List<int>();
        [SerializeField] private List<SerializableStruct> instances = new List<SerializableStruct>();
        [SerializeField] private List<int> ints = new List<int>();
        [SerializeField] private List<string> strings = new List<string>();
        [SerializeField] private List<float> floats = new List<float>();
        [SerializeField] private List<bool> bools = new List<bool>();
        [SerializeField] private List<int> references = new List<int>();
        [SerializeField] private List<Object> unityReferences = new List<Object>();
        [SerializeField] private List<ReferenceType> referenceTypes = new List<ReferenceType>();

        private Dictionary<Type, int> _referenceTypes = new Dictionary<Type, int>();
        private Dictionary<object, int> _dynamicReferences = new Dictionary<object, int>();

        private Dictionary<Type, TypeCustomizer> _customizers = new Dictionary<Type, TypeCustomizer>();

        public void AddConverter<TRuntime, TPersistent>(Func<TRuntime, TPersistent> pack, Func<TPersistent, TRuntime> unpack)
        {
            _customizers[typeof(TRuntime)] = new TypeCustomizer
            {
                pack = o => pack((TRuntime)o),
                unpack = o => unpack((TPersistent)o)
            };
        }

        public int Pack(object obj)
        {
            int instance = PackInternal(obj, dynamic: true);
            roots.Add(instance);
            return instance;
        }

        private int PackInternal(object obj, bool dynamic = false)
        {
            if (obj == null)
            {
                return -1;
            }

            if (dynamic)
            {
                if (_dynamicReferences.TryGetValue(obj, out var existingInstance))
                {
                    return existingInstance;
                }
            }

            Type originalType = obj.GetType();
            Type type = originalType;

            if (_customizers.TryGetValue(type, out var customizer))
            {
                obj = customizer.pack(obj);
                type = obj.GetType();
            }

            SerializableStruct instance = new SerializableStruct();

            instance.id = instances.Count;
            instances.Add(instance);

            foreach (FieldInfo field in type.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance))
            {
                if (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null ||
                    field.GetCustomAttribute<NonSerializedAttribute>() != null)
                {
                    continue;
                }

                Property property = new Property();
                property.value = new List<int>();
                property.name = field.Name;
                PackValue(field.GetValue(obj), ref property, field.GetCustomAttribute<SerializeReference>() != null);
                if (property.value.Count > 0)
                {
                    instance.properties.Add(property);
                }
            }

            if (dynamic)
            {
                _dynamicReferences[obj] = instance.id;
            }

            instance.type = PackType(type);
            instance.runtimeType = PackType(originalType);

            return instance.id;
        }

        private int PackType(Type type)
        {
            if (!_referenceTypes.TryGetValue(type, out int refType))
            {
                refType = referenceTypes.Count;
                referenceTypes.Add(new ReferenceType { assembly = type.Assembly.GetName().Name, type = type.FullName });
                _referenceTypes[type] = refType;
            }

            return refType;
        }

        private void PackValueInternal<T>(ref Property property, ValueType type, List<T> pool, T value)
        {
            int valueIndex = pool.Count;
            pool.Add(value);
            property.type = type;
            property.value.Add(valueIndex);
        }

        private void PackValue(object value, ref Property property, bool serializeReference)
        {
            if (value == null)
            {
                property.type = ValueType.Null;
                return;
            }

            Type originalType = value.GetType();
            Type type = originalType;

            property.runtimeType = PackType(originalType);

            if (_customizers.TryGetValue(type, out TypeCustomizer customizer))
            {
                value = customizer.pack(value);
                type = value.GetType();
            }

            if (type == typeof(int)) PackValueInternal(ref property, ValueType.Int, ints, (int)value);
            else if (type == typeof(string)) PackValueInternal(ref property, ValueType.String, strings, (string)value);
            else if (type == typeof(float)) PackValueInternal(ref property, ValueType.Float, floats, (float)value);
            else if (type == typeof(bool)) PackValueInternal(ref property, ValueType.Bool, bools, (bool)value);
            else if (typeof(Object).IsAssignableFrom(type))
                PackValueInternal(ref property, ValueType.UnityReference, unityReferences, (Object)value);
            else if (type.IsArray)
            {
                Array a = (Array)value;
                for (int i = 0; i < a.Length; i++)
                {
                    PackValue(a.GetValue(i), ref property, serializeReference);
                }
            }
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                IList a = (IList)value;
                foreach (object v in a)
                {
                    PackValue(v, ref property, serializeReference);
                }
            }
            else if (type.GetCustomAttribute<SerializableAttribute>() != null)
            {
                int reference = PackInternal(value, serializeReference);
                PackValueInternal(ref property, ValueType.Reference, references, reference);
            }
        }

        public Dictionary<int, object> Unpack()
        {
            Dictionary<string, Assembly> assemblies = AppDomain.CurrentDomain.GetAssemblies().ToDictionary(it => it.GetName().Name);
            Type[] types = new Type[referenceTypes.Count];
            for (var i = 0; i < referenceTypes.Count; i++)
            {
                ReferenceType referenceType = referenceTypes[i];
                types[i] = assemblies[referenceType.assembly].GetType(referenceType.type, throwOnError: true);
            }

            Dictionary<int, object> unpacked = new Dictionary<int, object>();
            foreach (SerializableStruct instance in instances)
            {
                Type type = types[instance.type];
                object o = Activator.CreateInstance(type);
                unpacked[instance.id] = o;
            }

            // unpacking order is inverse of packing order to correctly fill nested structs
            for (var index = instances.Count - 1; index >= 0; index--)
            {
                SerializableStruct instance = instances[index];
                Type runtimeType = types[instance.runtimeType];
                Type type = types[instance.type];
                object o = unpacked[instance.id];
                foreach (Property property in instance.properties)
                {
                    Type propertyRuntimeType = types[property.runtimeType];
                    FieldInfo field = type.GetField(property.name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                    if (field.FieldType.IsArray)
                    {
                        Array array = Array.CreateInstance(field.FieldType.GetElementType() ?? throw new Exception("WTF"), property.value.Count);
                        for (int i = 0; i < property.value.Count; i++)
                        {
                            array.SetValue(GetFromPool(property.type, propertyRuntimeType, property.value[i], unpacked), i);
                        }

                        field.SetValue(o, array);
                    }
                    else if (field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(List<>))
                    {
                        IList list = (IList)Activator.CreateInstance(field.FieldType);
                        foreach (int value in property.value)
                        {
                            list.Add(GetFromPool(property.type, propertyRuntimeType, value, unpacked));
                        }

                        field.SetValue(o, list);
                    }
                    else
                    {
                        field.SetValue(o, GetFromPool(property.type, propertyRuntimeType, property.value.Single(), unpacked));
                    }
                }

                if (_customizers.TryGetValue(runtimeType, out var customizer))
                {
                    o = customizer.unpack(o);
                }

                unpacked[instance.id] = o;
            }

            Dictionary<int, object> unpackedRoots = new Dictionary<int, object>();
            foreach (int root in roots)
            {
                unpackedRoots[root] = unpacked[root];
            }

            return unpackedRoots;
        }

        private object GetFromPool(ValueType type, Type runtimeType, int valueIndex, Dictionary<int, object> unpacked)
        {
            object raw = GetPoolRaw(type, valueIndex, unpacked);
            if (_customizers.TryGetValue(runtimeType, out var customizer))
            {
                return customizer.unpack(raw);
            }
            return raw;
        }

        private object GetPoolRaw(ValueType type, int valueIndex, Dictionary<int, object> unpacked)
        {
            switch (type)
            {
                case ValueType.Null:
                    return null;
                case ValueType.Int:
                    return ints[valueIndex];
                case ValueType.Float:
                    return floats[valueIndex];
                case ValueType.String:
                    return strings[valueIndex];
                case ValueType.Bool:
                    return bools[valueIndex];
                case ValueType.Reference:
                    return unpacked[references[valueIndex]];
                case ValueType.UnityReference:
                    return unityReferences[valueIndex];
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
    
    internal struct TypeCustomizer
    {
        internal Func<object, object> pack;
        internal Func<object, object> unpack;
    }

    [Serializable] internal struct ReferenceType
    {
        public string assembly;
        public string type;

        public override string ToString() {
            return $"{nameof(assembly)}: {assembly}, {nameof(type)}: {type}";
        }
    }

    [Serializable]
    internal class SerializableStruct
    {
        public int id;
        public List<Property> properties = new List<Property>();
        public int type = -1;
        public int runtimeType;

        public override string ToString() {
            return $"{nameof(properties)}: {properties.Count}, {nameof(type)}: {type}, {nameof(runtimeType)}: {runtimeType}";
        }
    }

    [Serializable]
    internal struct Property
    {
        public string name;
        public List<int> value;
        public ValueType type;
        public int runtimeType;

        public override string ToString()
        {
            return $"{nameof(name)}: {name}, {nameof(value)}: [{string.Join(",", value)}], {nameof(type)}: {type}, {nameof(runtimeType)}: {runtimeType}";
        }
    }

    internal enum ValueType
    {
        Null,
        Int,
        Float,
        String,
        Bool,
        Reference,
        UnityReference,
    }
}