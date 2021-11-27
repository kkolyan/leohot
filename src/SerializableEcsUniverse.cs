using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Leopotam.EcsLite;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Kk.LeoHot
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
            AddConverter<EcsPackedEntityWithWorld, int>(
                (runtime, ctx) =>
                {
                    if (!runtime.Unpack(out EcsWorld world, out int entity))
                    {
                        return 0;
                    }

                    TempEntityKey tempEntityKey = new TempEntityKey(ctx.worldToName[world], entity);
                    return ctx.entityToPackedId[tempEntityKey];
                },
                (persistent, ctx) =>
                {
                    if (persistent == 0)
                    {
                        return default;
                    }

                    TempEntityKey tempEntity = ctx.entityByPackedId[persistent];
                    return ctx.worldByName[tempEntity.world ?? ""].PackEntityWithWorld(tempEntity.entity);
                });

            AddIncomingLink<EcsEntityLink, EcsPackedEntityWithWorld>(
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
            CheckComponentsSerializable(ecsSystems);

            _packContext = new PackContext
            {
                entityToPackedId = new Dictionary<TempEntityKey, int>(),
                worldToName = new Dictionary<EcsWorld, string>()
            };

            Dictionary<string, EcsWorld> allNamedWorlds = ecsSystems.GetAllNamedWorlds();
            worlds = new SerializableWorld[1 + allNamedWorlds.Count];

            _packContext.worldToName[ecsSystems.GetWorld()] = null;
            foreach (KeyValuePair<string, EcsWorld> pair in ecsSystems.GetAllNamedWorlds())
            {
                _packContext.worldToName[pair.Value] = pair.Key;
            }

            PrepareSerializeWorld("", ecsSystems.GetWorld(), out worlds[0]);
            int i = 1;
            foreach (KeyValuePair<string, EcsWorld> entry in allNamedWorlds)
            {
                PrepareSerializeWorld(entry.Key, entry.Value, out worlds[i]);
                i++;
            }

            PackWorld("", ecsSystems.GetWorld(), ref worlds[0]);
            int j = 1;
            foreach (KeyValuePair<string, EcsWorld> entry in allNamedWorlds)
            {
                PackWorld(entry.Key, entry.Value, ref worlds[j]);
                j++;
            }

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

        private void CheckComponentsSerializable(EcsSystems ecsSystems)
        {
            CheckComponentsSerializable(ecsSystems.GetWorld());
            foreach (KeyValuePair<string, EcsWorld> entry in ecsSystems.GetAllNamedWorlds())
            {
                CheckComponentsSerializable(entry.Value);
            }
        }

        private void CheckComponentsSerializable(EcsWorld world)
        {
            IEcsPool[] pools = null;
            world.GetAllPools(ref pools);
            foreach (IEcsPool pool in pools)
            {
                Type type = pool.GetComponentType();
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
                worldByName = new Dictionary<string, EcsWorld>()
            };

            _unpackContext.worldByName[""] = ecsSystems.GetWorld();
            foreach (KeyValuePair<string, EcsWorld> pair in ecsSystems.GetAllNamedWorlds())
            {
                _unpackContext.worldByName[pair.Key] = pair.Value;
            }

            foreach (SerializableWorld serializedWorld in worlds)
            {
                EcsWorld world = ecsSystems.GetWorld(serializedWorld.name.Length > 0 ? serializedWorld.name : null);
                foreach (SerializableEntity serializableEntity in serializedWorld.entities)
                {
                    _unpackContext.entityByPackedId[serializableEntity.packedId] = new TempEntityKey(serializedWorld.name, world.NewEntity());
                }
            }

            Dictionary<int, object> unpacked = objects.Unpack();

            foreach (SerializableWorld serializedWorld in worlds)
            {
                EcsWorld world = ecsSystems.GetWorld(serializedWorld.name.Length > 0 ? serializedWorld.name : null);

                foreach (SerializableEntity entity in serializedWorld.entities)
                {
                    entity.cachedComponents = new object[entity.components.Count];
                    for (var i = 0; i < entity.components.Count; i++)
                    {
                        int component = entity.components[i];
                        object finalValue = unpacked[component];
                        if (finalValue != null)
                        {
                            MethodInfo template = typeof(EcsWorld).GetMethod(nameof(EcsWorld.GetPool));
                            MethodInfo getPool = template.MakeGenericMethod(finalValue.GetType());
                            IEcsPool pool = (IEcsPool)getPool.Invoke(world, Array.Empty<object>());
                            pool.AddRaw(_unpackContext.entityByPackedId[entity.packedId].entity, finalValue);
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
            int[] entities = null;
            world.GetAllEntities(ref entities);

            result.name = worldName;
            result.entities = new SerializableEntity[entities.Length];
            for (var i = 0; i < entities.Length; i++)
            {
                int entity = entities[i];
                _packContext.entityToPackedId[new TempEntityKey(worldName, entity)] = i + 1;
            }
        }

        private void PackWorld(string worldName, EcsWorld world, ref SerializableWorld result)
        {
            int[] entities = null;
            world.GetAllEntities(ref entities);

            for (var ei = 0; ei < entities.Length; ei++)
            {
                int entity = entities[ei];

                object[] components = null;
                world.GetComponents(entity, ref components);

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
                    packedId = _packContext.entityToPackedId[new TempEntityKey(worldName, entity)]
                };
            }
        }
    }

    public struct PackContext
    {
        public Dictionary<TempEntityKey, int> entityToPackedId;
        public Dictionary<EcsWorld, string> worldToName;
    }

    public delegate TPersistent Pack<in TRuntime, out TPersistent>(TRuntime runtime, PackContext ctx);

    public struct UnpackContext
    {
        public Dictionary<int, TempEntityKey> entityByPackedId;
        public Dictionary<string, EcsWorld> worldByName;
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
        public readonly string world;
        public readonly int entity;

        public TempEntityKey(string world, int entity)
        {
            this.world = string.IsNullOrEmpty(world) ? null : world;
            this.entity = entity;
        }

        public override string ToString()
        {
            return $"{nameof(world)}: {world}, {nameof(entity)}: {entity}";
        }

        public bool Equals(TempEntityKey other)
        {
            return world == other.world && entity == other.entity;
        }

        public override bool Equals(object obj)
        {
            return obj is TempEntityKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((world != null ? world.GetHashCode() : 0) * 397) ^ entity;
            }
        }
    }
}