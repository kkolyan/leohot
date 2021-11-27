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
        [SerializeField] private List<Sequence> sequences = new List<Sequence>();

        private Dictionary<Type, int> _referenceTypes = new Dictionary<Type, int>();
        private Dictionary<object, Value> _dynamicReferences = new Dictionary<object, Value>();

        private Dictionary<Type, TypeCustomizer> _customizers = new Dictionary<Type, TypeCustomizer>();

        private HashSet<Type> _forceSerializableTypes = new HashSet<Type>();

        public SerializableObjectContainer()
        {
            AddSerializable<Vector2>();
            AddSerializable<Vector3>();
            AddSerializable<Vector4>();
            AddSerializable<Rect>();
            AddSerializable<Quaternion>();
            AddSerializable<Matrix4x4>();
            AddSerializable<Color>();
            AddSerializable<Color32>();
            AddSerializable<LayerMask>();
            AddSerializable<AnimationCurve>();
            AddSerializable<Gradient>();
        }

        /// <summary>
        /// if you need serialize type without SerializableAttributes
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public void AddSerializable<T>()
        {
            Type type = typeof(T);
            if (type.GetCustomAttribute<SerializableAttribute>() != null)
            {
                Debug.LogWarning($"type already attributed with [Serializable]: {type}");
            }
            else
            {
                _forceSerializableTypes.Add(type);
            }
        }

        public void AddConverter<TRuntime, TPersistent>(Func<TRuntime, TPersistent> pack, Func<TPersistent, TRuntime> unpack)
        {
            _customizers[typeof(TRuntime)] = new TypeCustomizer
            {
                pack = o => pack((TRuntime)o),
                unpack = o => unpack((TPersistent)o),
                defaultPackedValue = typeof(TPersistent).IsValueType ? (object)Activator.CreateInstance<TPersistent>() : null
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
                if (property.value.type != ValueType.Null)
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
            return new Value(type, valueIndex, -1, originalType);
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
                return new Value(ValueType.Null, -1, -1, -1);
            }

            Type typeBeforeConvert = value.GetType();
            value = CustomizePack(value, typeBeforeConvert);
            Type typeAfterConvert = value.GetType();

            if (typeAfterConvert.IsArray || typeAfterConvert.IsGenericType && typeAfterConvert.GetGenericTypeDefinition() == typeof(List<>))
            {
                List<Value> elements = new List<Value>();
                Value persistentValue = new Value(ValueType.Sequence, sequences.Count, PackType(typeAfterConvert), PackType(typeBeforeConvert));
                sequences.Add(new Sequence { elements = elements });
                IList a = (IList)value;
                foreach (object v in a)
                {
                    Value item = PackValue(v, dynamic);
                    if (item.type == ValueType.Sequence)
                    {
                        throw new Exception("multidimensional arrays and lists not supported. " + typeBeforeConvert);
                    }

                    elements.Add(item);
                }

                return persistentValue;
            }

            if (typeAfterConvert == typeof(int)) return PackSingular(ValueType.Int, ints, (int)value, PackType(typeBeforeConvert));

            if (typeAfterConvert == typeof(string)) return PackSingular(ValueType.String, strings, (string)value, PackType(typeBeforeConvert));

            if (typeAfterConvert == typeof(float)) return PackSingular(ValueType.Float, floats, (float)value, PackType(typeBeforeConvert));

            if (typeAfterConvert == typeof(bool)) return PackSingular(ValueType.Bool, bools, (bool)value, PackType(typeBeforeConvert));

            if (typeof(Object).IsAssignableFrom(typeAfterConvert))
                return PackSingular(ValueType.UnityReference, unityReferences, (Object)value, PackType(typeBeforeConvert));

            if (typeAfterConvert.GetCustomAttribute<SerializableAttribute>() != null || _forceSerializableTypes.Contains(typeAfterConvert))
            {
                int reference = PackStruct(value);
                return PackSingular(ValueType.Reference, references, reference, PackType(typeBeforeConvert));
            }

            return new Value(ValueType.Null, -1, -1, -1);
        }

        private object CustomizePack(object value, Type originalType)
        {
            if (_customizers.TryGetValue(originalType, out TypeCustomizer customizer))
            {
                value = customizer.pack(value);
            }

            return value;
        }

        private object CustomizeUnpack(IReadOnlyList<Type> types, Value value, object o)
        {
            if (value.originalType >= 0 && _customizers.TryGetValue(types[value.originalType], out var customizer))
            {
                o = customizer.unpack(o ?? customizer.defaultPackedValue);
            }

            return o;
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

            result = CustomizeUnpack(types, value, result);

            return result;
        }

        private object UnpackValueInternal(IReadOnlyList<Type> types, Dictionary<int, object> unpacked, Value value)
        {
            if (value.type == ValueType.Sequence)
            {
                List<Value> elements = sequences[value.index].elements;
                Type type = types[value.convertedType];
                if (type.IsArray)
                {
                    Array array = Array.CreateInstance(type.GetElementType() ?? throw new Exception("WTF"), elements.Count);
                    for (int i = 0; i < elements.Count; i++)
                    {
                        Value singular = elements[i];
                        array.SetValue(UnpackValue(types, unpacked, singular), i);
                    }

                    return array;
                }

                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                {
                    IList list = (IList)Activator.CreateInstance(type);
                    foreach (Value singular in elements)
                    {
                        list.Add(UnpackValue(types, unpacked, singular));
                    }

                    return list;
                }

                throw new Exception($"unsupported container: {type}");
            }

            return UnpackSingular(value.type, value.index, unpacked);
        }

        private object UnpackSingular(ValueType type, int valueIndex, Dictionary<int, object> unpacked)
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
        internal object defaultPackedValue;
        internal Func<object, object> pack;
        internal Func<object, object> unpack;
    }

    [Serializable]
    internal struct Sequence
    {
        public List<Value> elements;
    }

    [Serializable]
    internal struct Value
    {
        public ValueType type;
        public int index;
        // after applying customizer packing
        public int convertedType;
        // before last customizer packing. but maybe after previous, because they can be chained in some cases (with the value and then with array items or struct fields)
        public int originalType;

        public Value(ValueType type, int index, int convertedType, int originalType)
        {
            this.type = type;
            this.index = index;
            this.convertedType = convertedType;
            this.originalType = originalType;
        }

        public override string ToString()
        {
            return
                $"{nameof(type)}: {type}, {nameof(index)}: {index}, {nameof(originalType)}: {originalType}, {nameof(convertedType)}: {convertedType}";
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
        Null,
        Int,
        Float,
        String,
        Bool,
        Reference,
        UnityReference,
        Sequence,
    }
}