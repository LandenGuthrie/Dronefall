using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

public class DroneManager : MonoBehaviour
{
    public const int CACHED_DRONE_COUNT = 8;
    
    [SerializeField] private List<ParticleSystem> ExplosionParticles;
    [SerializeField] private List<DroneDefinition> Drones;

    public LayerMask PlaceableLayers;
    public KeyCode PlaceKey;
    public KeyCode CancelKey;
    
    public void Update()
    {
        if (Input.GetKeyDown(KeyCode.Mouse2))
        {
            UseDrone(DroneType.Default);
        }
    }

    public void InitializeDroneManager() => CreateDroneCache();
    private void CreateDroneCache()
    {
        var droneTypesCount = Enum.GetNames(typeof(DroneType)).Length;
        for (var i = 0; i < droneTypesCount; i++)
        {
            for (var j = 0; j < CACHED_DRONE_COUNT; j++)
            {
                var type = (DroneType)i;
                var droneInstance = CreateDrone(type);
                _cachedDronePool.Add(droneInstance, (true, type));
            }
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
        var drone = GetCachedDroneFromPool(type);
        _cachedDronePool[drone] = (false, type);
        
        drone.SetActive(true);
        drone.GetComponent<DroneBase>().UseDrone();
    }
    public void ReturnDroneToPool(GameObject droneInstance)
    {
        var droneType = droneInstance.TryGetComponent(out DroneBase droneComp)
            ? droneComp.Type
            : throw new Exception("Drone does not contain a 'DroneBase' component.");

        droneComp.ResetDrone();
        
        droneInstance.SetActive(false);
        droneInstance.transform.position = Vector3.zero;
        droneInstance.transform.rotation = Quaternion.identity;
        
        _cachedDronePool[droneInstance] = (true, droneType);
    }
    public void ReturnAllDronesToPool()
    {
        for (var i = 0; i < _cachedDronePool.Count; i++)
        {
            var (instance, (inPool, type)) = _cachedDronePool.ElementAt(i);
            if (inPool) continue;
            ReturnDroneToPool(instance);
        }
    }
    
    // --- Util ---
    private GameObject CreateDrone(DroneType type)
    {
        var dronePrefab = GetDronePrefabFromType(type);
        var droneInstance = Instantiate(dronePrefab, transform);
        droneInstance.SetActive(false);
        return droneInstance;
    }
    private GameObject GetCachedDroneFromPool(DroneType type)
    {
        foreach (var enemy in _cachedDronePool.Where(enemy => enemy.Value.inPool && enemy.Value.droneType == type))
            return enemy.Key;
        throw new Exception("No drone available with the given type.");
    }
    private GameObject GetDronePrefabFromType(DroneType type)
    {
        foreach (var drone in Drones)
        {
            if (drone.Type == type) return drone.Prefab;
        }
        throw new Exception("Could not find drone prefab for type: " + type);
    }
    
    private readonly Dictionary<GameObject, (bool inPool, DroneType droneType)> _cachedDronePool = new();
}

[Serializable]
public struct DroneDefinition
{
    public DroneType Type;
    public GameObject Prefab;
}
