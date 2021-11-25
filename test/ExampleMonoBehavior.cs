using Leopotam.EcsLite;
using UnityEngine;

namespace Kk.LeoHot
{
    public class ExampleMonoBehavior: MonoBehaviour
    {
        [SerializeField] private SerializableEcsUniverse universe;

        private EcsSystems _ecsSystems;

        private void OnEnable()
        {
            _ecsSystems = new EcsSystems(new EcsWorld());
            _ecsSystems.Init();
            
#if UNITY_EDITOR
            universe.UnpackState(_ecsSystems);
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            universe.PackState(_ecsSystems);
#endif
        }
    }
}