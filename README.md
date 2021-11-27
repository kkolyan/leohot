
# About

Unity hot-reload extension for [LeoECS Lite](https://github.com/Leopotam/ecslite).

*Hot-reload* (officialy [Domain Reloading](https://docs.unity3d.com/2020.3/Documentation/Manual/DomainReloading.html)) is the feature of Unity Editor that applies script changes without exiting Play Mode. 
It works by default (at least in 2021 and earlier), but can be switched off in Editor Settings.

Both LeoECS (both lite and classic) frameworks doesn't support this mode in stock and requires 
hot-reload switched off to avoid game crash. This library allow to enable it back.

*__Notice__: Though, hot-reload may increase developer productivity, it also it requires practice and attention for good flight, because easily leads game session into incorrect state*

# Features
* Based on built-in Unity serialization
* Entity cross-references support

# Implementation notes

### Serialization
In order to support cross-references, library deeply transforms components using reflection 
before leaving it to Unity's serializer. But transformer mimics Unity's serialization rules, so you shouldn't care.

### Performance
Implementation is simple and allocation intensive. Need to test if that's ok at least on common dataset sizes.

# Installation
Add following line to `Packages/manifest.json`'s `dependencies` section:
```
"com.nplekhanov.csx.leohot": "https://github.com/kkolyan/leohot.git",
```

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

# Projects powered by

* Hot-reload-enchanted version of the LeoECS community demo game: https://github.com/kkolyan/3D-Platformer-Lite-Hot.
