using System;
using System.Collections.Generic;
using System.Reflection;
using Leopotam.EcsLite;
using UnityEngine;

namespace Kk.LeoHot
{
    [Serializable]
    public class SerializableEcsUniverse
    {
        [SerializeField] private SerializableWorld[] worlds;
        [SerializeField] private SerializableObjectContainer componentData = new SerializableObjectContainer();

        private PackContext _packContext;
        private UnpackContext _unpackContext;

        public SerializableEcsUniverse()
        {
            
            AddConverter<EcsPackedEntityWithWorld, int>(
                (runtime, ctx) =>
                {
                    if (!runtime.Unpack(out EcsWorld world, out int entity))
                        return 0;
                    TempEntityKey tempEntityKey = new TempEntityKey(ctx.worldToName[world], entity);
                    return ctx.entityToPackedId[tempEntityKey];
                },
                (persistent, ctx) =>
                {
                    TempEntityKey tempEntity = ctx.entityByPackedId[persistent];
                    return ctx.worldByName[tempEntity.world ?? ""].PackEntityWithWorld(tempEntity.entity);
                });
        }

        public void AddConverter<TRuntime, TPersistent>(
            Pack<TRuntime, TPersistent> pack,
            Unpack<TPersistent, TRuntime> unpack
        )
        {
            componentData.AddConverter<TRuntime, TPersistent>(
                runtime => pack(runtime, _packContext),
                persistent => unpack(persistent, _unpackContext)
            );
        }

        public void PackState(EcsSystems ecsSystems)
        {
            Dictionary<string, EcsWorld> allNamedWorlds = ecsSystems.GetAllNamedWorlds();
            worlds = new SerializableWorld[1 + allNamedWorlds.Count];

            Dictionary<EcsWorld, string> nameByWorld = new Dictionary<EcsWorld, string>();
            nameByWorld[ecsSystems.GetWorld()] = null;
            foreach (KeyValuePair<string, EcsWorld> pair in ecsSystems.GetAllNamedWorlds())
            {
                nameByWorld[pair.Value] = pair.Key;
            }

            PrepareSerializeWorld("", ecsSystems.GetWorld(), out worlds[0], nameByWorld);
            int i = 1;
            foreach (KeyValuePair<string, EcsWorld> entry in allNamedWorlds)
            {
                PrepareSerializeWorld(entry.Key, entry.Value, out worlds[i], nameByWorld);
                i++;
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
            foreach (KeyValuePair<string,EcsWorld> pair in ecsSystems.GetAllNamedWorlds())
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

            Dictionary<int, object> unpacked = componentData.Unpack();

            foreach (SerializableWorld serializedWorld in worlds)
            {
                EcsWorld world = ecsSystems.GetWorld(serializedWorld.name.Length > 0 ? serializedWorld.name : null);

                foreach (SerializableEntity entity in serializedWorld.entities)
                {
                    foreach (int component in entity.components)
                    {
                        object finalValue = unpacked[component];
                        if (finalValue != null)
                        {
                            MethodInfo template = typeof(EcsWorld).GetMethod(nameof(EcsWorld.GetPool));
                            MethodInfo getPool = template.MakeGenericMethod(finalValue.GetType());
                            IEcsPool pool = (IEcsPool)getPool.Invoke(world, Array.Empty<object>());
                            pool.AddRaw(_unpackContext.entityByPackedId[entity.packedId].entity, finalValue);
                        }
                    }
                }
            }
        }

        private void PrepareSerializeWorld(string worldName, EcsWorld world, out SerializableWorld result, Dictionary<EcsWorld, string> nameByWorld)
        {
            int[] entities = null;
            world.GetAllEntities(ref entities);

            result.name = worldName;
            result.entities = new SerializableEntity[entities.Length];

            _packContext = new PackContext
            {
                entityToPackedId = new Dictionary<TempEntityKey, int>(),
                worldToName = nameByWorld
            };
            for (var i = 0; i < entities.Length; i++)
            {
                int entity = entities[i];
                _packContext.entityToPackedId[new TempEntityKey(worldName, entity)] = i + 1;
            }

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

                    serializableComponents.Add(componentData.Pack(t));
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
    }

    [Serializable]
    internal class SerializableEntity
    {
        public int packedId;
        public List<int> components;
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

        public override string ToString() {
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