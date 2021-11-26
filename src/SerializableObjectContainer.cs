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
        [SerializeField] private List<Value> roots = new List<Value>();
        [SerializeField] private List<SerializableStruct> structs = new List<SerializableStruct>();
        [SerializeField] private List<int> ints = new List<int>();
        [SerializeField] private List<string> strings = new List<string>();
        [SerializeField] private List<float> floats = new List<float>();
        [SerializeField] private List<bool> bools = new List<bool>();
        [SerializeField] private List<int> references = new List<int>();
        [SerializeField] private List<Object> unityReferences = new List<Object>();
        [SerializeField] private List<ReferenceType> referenceTypes = new List<ReferenceType>();

        private Dictionary<Type, int> _referenceTypes = new Dictionary<Type, int>();
        private Dictionary<object, Value> _dynamicReferences = new Dictionary<object, Value>();

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
            Value value = PackValue(obj, true);
            var id = roots.Count;
            roots.Add(value);
            return id;
        }

        private int PackStruct(object obj)
        {
            if (obj == null)
            {
                return -1;
            }

            SerializableStruct instance = new SerializableStruct();

            instance.id = structs.Count;
            instance.type = PackType(obj.GetType());
            structs.Add(instance);

            foreach (FieldInfo field in obj.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance))
            {
                if (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null ||
                    field.GetCustomAttribute<NonSerializedAttribute>() != null)
                {
                    continue;
                }

                Property property = new Property();
                property.name = field.Name;
                property.value = PackValue(field.GetValue(obj), field.GetCustomAttribute<SerializeReference>() != null);
                if (property.value.elements != null || property.value.ownType != ValueType.None)
                {
                    instance.properties.Add(property);
                }
            }

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

        private Value PackSingular<T>(ValueType type, ICollection<T> pool, T value, int originalType)
        {
            int valueIndex = pool.Count;
            pool.Add(value);
            return new Value
            {
                runtimeType = originalType,
                storedType = -1,
                ownType = type,
                ownIndex = valueIndex
            };
        }

        private Value PackValue(object value, bool dynamic)
        {
            if (dynamic)
            {
                if (_dynamicReferences.TryGetValue(value, out var existingInstance))
                {
                    return existingInstance;
                }
            }

            return PackValueInternal(value, dynamic);
        }

        private Value PackValueInternal(object value, bool dynamic)
        {
            if (value == null)
            {
                return new Value();
            }

            Type typeBeforeConvert = value.GetType();
            value = CustomizePack(value, typeBeforeConvert);
            Type typeAfterConvert = value.GetType();

            if (typeAfterConvert.IsArray)
            {
                Value persistentValue = new Value
                {
                    elements = new List<Value>(),
                    runtimeType = PackType(typeBeforeConvert),
                    storedType = PackType(typeAfterConvert)
                };
                Array a = (Array)value;
                for (int i = 0; i < a.Length; i++)
                {
                    Value item = PackValue(a.GetValue(i), dynamic);
                    if (item.elements != null)
                    {
                        throw new Exception("multidimensional arrays and lists not supported. " + typeBeforeConvert);
                    }

                    persistentValue.elements.Add(item);
                }

                return persistentValue;
            }

            if (typeAfterConvert.IsGenericType && typeAfterConvert.GetGenericTypeDefinition() == typeof(List<>))
            {
                Value persistentValue = new Value
                {
                    elements = new List<Value>(),
                    runtimeType = PackType(typeBeforeConvert),
                    storedType = PackType(typeAfterConvert)
                };
                IList a = (IList)value;
                foreach (object v in a)
                {
                    Value item = PackValue(v, dynamic);
                    if (item.elements != null)
                    {
                        throw new Exception("multidimensional arrays and lists not supported. " + typeBeforeConvert);
                    }

                    persistentValue.elements.Add(item);
                }

                return persistentValue;
            }

            if (typeAfterConvert == typeof(int)) return PackSingular(ValueType.Int, ints, (int)value, PackType(typeBeforeConvert));

            if (typeAfterConvert == typeof(string)) return PackSingular(ValueType.String, strings, (string)value, PackType(typeBeforeConvert));

            if (typeAfterConvert == typeof(float)) return PackSingular(ValueType.Float, floats, (float)value, PackType(typeBeforeConvert));

            if (typeAfterConvert == typeof(bool)) return PackSingular(ValueType.Bool, bools, (bool)value, PackType(typeBeforeConvert));

            if (typeof(Object).IsAssignableFrom(typeAfterConvert))
                return PackSingular(ValueType.UnityReference, unityReferences, (Object)value, PackType(typeBeforeConvert));

            if (typeAfterConvert.GetCustomAttribute<SerializableAttribute>() != null)
            {
                int reference = PackStruct(value);
                return PackSingular(ValueType.Reference, references, reference, PackType(typeBeforeConvert));
            }

            return new Value();
        }

        private object CustomizePack(object value, Type originalType)
        {
            if (_customizers.TryGetValue(originalType, out TypeCustomizer customizer))
            {
                value = customizer.pack(value);
            }

            return value;
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
            foreach (SerializableStruct instance in structs)
            {
                Type type = types[instance.type];
                object o = Activator.CreateInstance(type);
                unpacked[instance.id] = o;
            }

            // unpacking order is inverse of packing order to correctly fill nested structs
            for (var index = structs.Count - 1; index >= 0; index--)
            {
                SerializableStruct instance = structs[index];
                Type type = types[instance.type];
                object o = unpacked[instance.id];
                foreach (Property property in instance.properties)
                {
                    FieldInfo field = type.GetField(property.name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                    object propertyValue = UnpackValue(types, unpacked, property.value);
                    field.SetValue(o, propertyValue);
                }

                unpacked[instance.id] = o;
            }

            Dictionary<int, object> unpackedRoots = new Dictionary<int, object>();
            for (var i = 0; i < roots.Count; i++)
            {
                object value = UnpackValue(types, unpacked, roots[i]);

                unpackedRoots[i] = value;
            }

            return unpackedRoots;
        }

        private object UnpackValue(IReadOnlyList<Type> types, Dictionary<int, object> unpacked, Value value)
        {
            object result = UnpackValueInternal(types, unpacked, value);

            if (_customizers.TryGetValue(types[value.runtimeType], out var customizer))
            {
                result = customizer.unpack(result);
            }

            return result;
        }

        private object UnpackValueInternal(IReadOnlyList<Type> types, Dictionary<int, object> unpacked, Value value)
        {
            if (value.storedType >= 0)
            {
                Type type = types[value.storedType];
                if (type.IsArray)
                {
                    Array array = Array.CreateInstance(type.GetElementType() ?? throw new Exception("WTF"), value.elements.Count);
                    for (int i = 0; i < value.elements.Count; i++)
                    {
                        Value singular = value.elements[i];
                        array.SetValue(UnpackValue(types, unpacked, singular), i);
                    }

                    return array;
                }

                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                {
                    IList list = (IList)Activator.CreateInstance(type);
                    foreach (Value singular in value.elements)
                    {
                        list.Add(UnpackValue(types, unpacked, singular));
                    }

                    return list;
                }
            }

            return UnpackSingular(value.ownType, value.ownIndex, unpacked);
        }

        private object UnpackSingular(ValueType type, int valueIndex, Dictionary<int, object> unpacked)
        {
            switch (type)
            {
                case ValueType.None:
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

    [Serializable]
    internal struct Value
    {
        public List<Value> elements;
        public ValueType ownType;
        public int ownIndex;
        public int storedType;
        public int runtimeType;

        public override string ToString()
        {
            return
                $"{nameof(ownType)}: {ownType}, {nameof(ownIndex)}: {ownIndex}, {nameof(runtimeType)}: {runtimeType}, {nameof(storedType)}: {storedType}, {nameof(elements)}: [{(elements == null ? "" : string.Join(",", elements))}]";
        }
    }

    [Serializable]
    internal struct ReferenceType
    {
        public string assembly;
        public string type;

        public override string ToString()
        {
            return $"{nameof(assembly)}: {assembly}, {nameof(type)}: {type}";
        }
    }

    [Serializable]
    internal class SerializableStruct
    {
        public int id;
        public List<Property> properties = new List<Property>();
        public int type = -1;

        public override string ToString()
        {
            return $"{nameof(properties)}: {properties.Count}, {nameof(type)}: {type}";
        }
    }

    [Serializable]
    internal struct Property
    {
        public string name;
        public Value value;

        public override string ToString()
        {
            return $"{nameof(name)}: {name}, {nameof(value)}: {value}";
        }
    }

    internal enum ValueType
    {
        None,
        Int,
        Float,
        String,
        Bool,
        Reference,
        UnityReference,
    }
}