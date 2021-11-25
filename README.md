
# About

Unity hot-reload extension for [LeoECS Lite](https://github.com/Leopotam/ecslite)

# Features
* Based on built-in Unity serialization
* Entity cross-references support

# Implementation notes

## Serialization
In order to support cross-references, library deeply transforms components using reflection 
before leaving it to Unity's serializer. But transformer mimics Unity's serialization rules, so you shouldn't care.

## Performance
Implementation is simple and allocation intensive. Need to test if that's ok.

# How to use
Note that usual ECS initialization (systems and worlds definitions) moved from `Start` to `OnEnable` - that's mandatory.
```c#

public class MyMonoBehavior: MonoBehaviour
{
    [SerializeField] private SerializableEcsUniverse packedUniverse;

    private EcsSystems _ecsSystems;

    private void OnEnable()
    {
        // usual ECS systems and worlds initialization
        _ecsSystems = new EcsSystems(...);
        ...
        _ecsSystems.Init();
        
#if UNITY_EDITOR
        if (packedUniverse == null) 
        {
            packedUniverse = new SerializableEcsUniverse();
        }
        
        // restore components and entities that were serialized during hot-reload
        // that's safe to call even first time when there nothing to restore
        packedUniverse.UnpackState(_ecsSystems);
#endif
    }
    
    private void Update() 
    {
        // usual update actions
        _ecsSystems.Run();
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        // dump components and entities to the hot-reload friendly format
        packedUniverse.PackState(_ecsSystems);
#endif
    }
}

```

