using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

public class DroneManager : MonoBehaviour
{
    [SerializeField] private List<ParticleSystem> ExplosionParticles;
    [SerializeField] private List<DroneDefinition> Drones;

    [Header("Settings")]
    [SerializeField] private LayerMask PlaceableLayers;
    public int DroneCollidable;
    public KeyCode PlaceKey;
    public KeyCode CancelKey;
    public KeyCode PlaceDrone;
    
    public bool CanSpawnDrone { get; set; } = true;
    
    public event Action<DroneBase> DronePlaced;
    
    public void Update()
    {
        if (Input.GetKeyDown(PlaceDrone) && CanSpawnDrone)
        {
            UseDrone(DroneType.Default);
            CanSpawnDrone = false;
        }
    }

    public void InitializeDroneManager() => CreateDroneCache();
    private void CreateDroneCache()
    {
        var droneTypesCount = Enum.GetNames(typeof(DroneType)).Length;
        for (var i = 0; i < droneTypesCount; i++)
        {
            var type = (DroneType)i;
            var pool = new DronePool(16, 16, transform, GetDronePrefabFromType(type));
            _dronePools.Add(type, pool);
        }
    }

    // --- Execution ---
    public void PlayExplosionParticle(Vector3 position)
    {
        var particle = ExplosionParticles[Random.Range(0, ExplosionParticles.Count)];
        particle.transform.position = position;
        particle.gameObject.SetActive(true);
        particle.Play();
    }
    public bool GetMousePositionOnSelectableLayers(out Vector3 position)
    {
        // Getting raycast
        position = Vector3.zero;
        
        var ray = GameManager.Instance.PlayerCamera.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out var hit, float.PositiveInfinity, PlaceableLayers)) return false;
        if (hit.collider.gameObject == GameManager.Instance.TerrainGenerator.gameObject) return false;
        
        // Setting drone position
        position = hit.point;

        return true;
    }
    
    public void UseDrone(DroneType type)
    {
        var drone = _dronePools[type].GetFromPool();
        
        drone.SetActive(true);
        if (drone.TryGetComponent(out DroneBase droneComp))
        {
            DronePlaced?.Invoke(droneComp);
            droneComp.UseDrone();
        }
    }
    public void ReturnDroneToPool(GameObject drone, DroneType type)
    {
        _dronePools[type].ReturnToPool(drone);
    }
    public void ReturnAllDronesToPool()
    {
        foreach (var pool in _dronePools)
        {
            pool.Value.ReturnAllToPool();
        }
    }
    
    // --- Util ---
    public List<GameObject> GetActiveDrones()
    {
        var activeDrones = new List<GameObject>();
        foreach (var pool in _dronePools)
        {
            activeDrones.AddRange(pool.Value.GetObjectsOutOfPool());
        }
        return activeDrones;
    }
    private GameObject GetDronePrefabFromType(DroneType type)
    {
        foreach (var drone in Drones.Where(drone => drone.Type == type))
        {
            return drone.Prefab;
        }

        throw new Exception("Could not find drone prefab for type: " + type);
    }

    private readonly Dictionary<DroneType, DronePool> _dronePools = new();
}

public class DronePool : ObjectPool<GameObject>
{
    public DronePool(int count, int expansion, 
        Transform parent, GameObject prefab) 
        : base(count, expansion, () => CreateDrone(parent, prefab)) {}
    
    private static GameObject CreateDrone(Transform parent, GameObject prefab)
    {
        var enemyInstance = GameObject.Instantiate(prefab, Vector3.zero, Quaternion.identity, parent);
        enemyInstance.SetActive(false);
        return enemyInstance;
    }
    
    public override void OnObjectTakenFromPool(GameObject obj)
    {
        base.OnObjectTakenFromPool(obj);
        obj.SetActive(true);
    }
    public override void OnObjectReturnedToPool(GameObject obj)
    {
        base.OnObjectReturnedToPool(obj);
        
        if (obj.TryGetComponent<DroneBase>(out var droneComp)) droneComp.ResetDrone();
        obj.transform.position = Vector3.zero;
        obj.transform.rotation = Quaternion.identity;
        obj.SetActive(false);
    }
    public override void OnObjectDestroyed(GameObject obj)
    {
        base.OnObjectDestroyed(obj);
        GameObject.Destroy(obj);
    }
}


[Serializable]
public struct DroneDefinition
{
    public DroneType Type;
    public GameObject Prefab;
}
