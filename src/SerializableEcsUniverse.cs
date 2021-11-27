using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Leopotam.Ecs;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Kk.LeoHotClassic
{
    [Serializable]
    public class SerializableEcsUniverse
    {
        [SerializeField] private SerializableWorld[] worlds;
        [SerializeField] private SerializableObjectContainer objects = new SerializableObjectContainer();

        private PackContext _packContext;
        private UnpackContext _unpackContext;

        [SerializeField] private List<IncomingLinkState> unityLinks;

        private List<IncomingLink> _linkedObjectDefs = new List<IncomingLink>();

        [Serializable]
        private struct IncomingLinkState
        {
            public Object linkOwner;
            public int packedStateId;
        }

        private struct IncomingLink
        {
            internal Type ownerType;
            internal Func<Object, object> pack;
            internal Action<Object, object> unpack;
        }

        public SerializableEcsUniverse()
        {
            AddConverter<EcsEntity, int>(
                (runtime, ctx) =>
                {
                    if (!runtime.IsAlive())
                    {
                        return 0;
                    }

                    TempEntityKey tempEntityKey = new TempEntityKey(runtime);
                    return ctx.entityToPackedId[tempEntityKey];
                },
                (persistent, ctx) =>
                {
                    if (persistent == 0)
                    {
                        return default;
                    }

                    TempEntityKey tempEntity = ctx.entityByPackedId[persistent];
                    return tempEntity.entity;
                });

            AddIncomingLink<EcsEntityLink, EcsEntity>(
                link => link.entity,
                (link, entity) => link.entity = entity
            );
        }

        public void AddIncomingLink<TUnityObject, TEcsState>(
            Func<TUnityObject, TEcsState> pack,
            Action<TUnityObject, TEcsState> unpack
        ) where TUnityObject : Object
        {
            _linkedObjectDefs.Add(new IncomingLink
            {
                ownerType = typeof(TUnityObject),
                pack = o => pack((TUnityObject)o),
                unpack = (uo, o) => unpack((TUnityObject)uo, (TEcsState)o)
            });
        }

        public void AddConverter<TRuntime, TPersistent>(
            Pack<TRuntime, TPersistent> pack,
            Unpack<TPersistent, TRuntime> unpack
        )
        {
            objects.AddConverter<TRuntime, TPersistent>(
                runtime => pack(runtime, _packContext),
                persistent => unpack(persistent, _unpackContext)
            );
        }

        public void PackState(EcsSystems ecsSystems)
        {
            CheckComponentsSerializable(ecsSystems.World);

            _packContext = new PackContext
            {
                entityToPackedId = new Dictionary<TempEntityKey, int>()
            };

            worlds = new SerializableWorld[1];

            PrepareSerializeWorld("", ecsSystems.World, out worlds[0]);
            PackWorld(ecsSystems.World, ref worlds[0]);

            unityLinks = new List<IncomingLinkState>();
            foreach (IncomingLink def in _linkedObjectDefs)
            {
                foreach (Object o in Object.FindObjectsOfType(def.ownerType))
                {
                    object value = def.pack(o);
                    int valueId = objects.Pack(value);
                    unityLinks.Add(new IncomingLinkState
                    {
                        packedStateId = valueId,
                        linkOwner = o
                    });
                }
            }
        }

        private void CheckComponentsSerializable(EcsWorld world)
        {
            foreach (IEcsComponentPool pool in world.ComponentPools)
            {
                if (pool == null)
                {
                    continue;
                }
                Type type = pool.ItemType;
                if (type.GetCustomAttribute<SerializableAttribute>() == null)
                {
                    Debug.LogError($"component is not serializable: {type}");
                }
            }
        }

        public void UnpackState(EcsSystems ecsSystems)
        {
            _unpackContext = new UnpackContext
            {
                entityByPackedId = new Dictionary<int, TempEntityKey>(),
                world = ecsSystems.World
            };

            SerializableWorld serializedWorld = worlds[0];
            {
                foreach (SerializableEntity serializableEntity in serializedWorld.entities)
                {
                    _unpackContext.entityByPackedId[serializableEntity.packedId] = new TempEntityKey(ecsSystems.World.NewEntity());
                }
            }

            Dictionary<int, object> unpacked = objects.Unpack();

            {
                foreach (SerializableEntity entity in serializedWorld.entities)
                {
                    entity.cachedComponents = new object[entity.components.Count];
                    for (var i = 0; i < entity.components.Count; i++)
                    {
                        int component = entity.components[i];
                        object finalValue = unpacked[component];
                        if (finalValue != null)
                        {
                            MethodInfo replaceTemplate = typeof(EcsEntityExtensions).GetMethod(nameof(EcsEntityExtensions.Replace));
                            MethodInfo replace = replaceTemplate.MakeGenericMethod(finalValue.GetType());
                            EcsEntity ecsEntity = _unpackContext.entityByPackedId[entity.packedId].entity;
                            replace.Invoke(null, new[] { ecsEntity, finalValue });
                        }

                        entity.cachedComponents[i] = finalValue;
                    }
                }
            }

            Dictionary<Type, IncomingLink> defs = _linkedObjectDefs.ToDictionary(it => it.ownerType);

            foreach (IncomingLinkState o in unityLinks)
            {
                object state = unpacked[o.packedStateId];
                defs[o.linkOwner.GetType()].unpack(o.linkOwner, state);
            }
        }

        private void PrepareSerializeWorld(string worldName, EcsWorld world, out SerializableWorld result)
        {
            EcsEntity[] entities = null;
            world.GetAllEntities(ref entities);

            result.name = worldName;
            result.entities = new SerializableEntity[entities.Length];
            for (var i = 0; i < entities.Length; i++)
            {
                EcsEntity entity = entities[i];
                _packContext.entityToPackedId[new TempEntityKey(entity)] = i + 1;
            }
        }

        private void PackWorld(EcsWorld world, ref SerializableWorld result)
        {
            EcsEntity[] entities = null;
            world.GetAllEntities(ref entities);

            for (var ei = 0; ei < entities.Length; ei++)
            {
                EcsEntity entity = entities[ei];

                object[] components = null;
                entity.GetComponentValues(ref components);

                List<int> serializableComponents = new List<int>();
                foreach (object t in components)
                {
                    if (t == null)
                    {
                        continue;
                    }

                    serializableComponents.Add(objects.Pack(t));
                }

                result.entities[ei] = new SerializableEntity
                {
                    components = serializableComponents,
                    packedId = _packContext.entityToPackedId[new TempEntityKey(entity)]
                };
            }
        }
    }

    public struct PackContext
    {
        public Dictionary<TempEntityKey, int> entityToPackedId;
    }

    public delegate TPersistent Pack<in TRuntime, out TPersistent>(TRuntime runtime, PackContext ctx);

    public struct UnpackContext
    {
        public Dictionary<int, TempEntityKey> entityByPackedId;
        public EcsWorld world;
    }

    public delegate TRuntime Unpack<in TPersistent, out TRuntime>(TPersistent persistent, UnpackContext ctx);

    [Serializable]
    internal struct SerializableWorld
    {
        public string name;
        public SerializableEntity[] entities;

        public override string ToString()
        {
            return $"{nameof(name)}: {name}, {nameof(entities)}: {entities.Length}";
        }
    }

    [Serializable]
    internal class SerializableEntity
    {
        public int packedId;
        public List<int> components;
        public object[] cachedComponents;

        public override string ToString()
        {
            return
                $"{nameof(packedId)}: {packedId}, {nameof(cachedComponents)}: ({components?.Count})[{string.Join(", ", cachedComponents?.Select(it => (it?.GetType())?.Name) ?? new List<string>())}]";
        }
    }

    public readonly struct TempEntityKey : IEquatable<TempEntityKey>
    {
        public readonly EcsEntity entity;

        public TempEntityKey(EcsEntity entity)
        {
            this.entity = entity;
        }

        public override string ToString()
        {
            return $"{nameof(entity)}: {entity}";
        }

        public bool Equals(TempEntityKey other)
        {
            return entity == other.entity;
        }

        public override bool Equals(object obj)
        {
            return obj is TempEntityKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return entity.GetHashCode();
        }
    }
}