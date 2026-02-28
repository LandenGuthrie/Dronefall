using System;
using System.Collections.Generic;
using UnityEngine;

public class DroneBase : MonoBehaviour
{
    public const float INITIAL_PLACEMENT_HEIGHT = 5;
    public const float MINIMUM_REACHED_DISTANCE = 0.05f;
    
    [SerializeField] private DroneSettings DroneSettings;
    [SerializeField] private DroneControlSettings ControlSettings;

    public DroneType Type;
    
    public Vector3 StartPosition { get; private set; }
    public Vector3 TargetPosition { get; private set; }
    public bool TargetReached { get; private set; }
    public DroneState CurrentState { get; private set; }
    
    public void Update() => OnUpdate();
    public void OnDrawGizmos()
    {
        if (CurrentState == DroneState.Flying) Gizmos.DrawSphere(TargetPosition, 0.5f);
        Gizmos.DrawSphere(transform.position, DroneSettings.CollisionRadius);
    }

    public void OnCollisionEnter(Collision other)
    {
        if (other.gameObject.layer == GameManager.Instance.DroneManager.DroneCollidable)
        {
            GameManager.Instance.DroneManager.PlayExplosionParticle(transform.position);
            AudioManager.Instance.PlayAtPosition("Explosion", transform.position, 200, 2);
            _droneSounds.Stop();
            GameManager.Instance.DroneManager.ReturnDroneToPool(gameObject, Type);
        }
    }

    public virtual void OnUpdate()
    {
        if (TargetReached || CurrentState == DroneState.None) return;

        // Updating visuals
        UpdateDroneVisuals();
        
        // Updating wings
        foreach (var wing in DroneSettings.Wings)
        {
            wing.Rotate(new Vector3(0, Time.deltaTime * ControlSettings.BaseSpeed * 100, 0));
        }
        
        switch (CurrentState)
        {
            case DroneState.Positioning:
                UpdateDroneStartPosition();
                if (Input.GetKeyDown(GameManager.Instance.DroneManager.PlaceKey))
                {
                    CurrentState = DroneState.AdjustingHeight;
                }
                break;
            case DroneState.AdjustingHeight:
                UpdateDroneHeight();
                if (Input.GetKeyDown(GameManager.Instance.DroneManager.CancelKey))
                {
                    CurrentState = DroneState.Positioning;
                    return;
                }
                if (Input.GetKeyUp(GameManager.Instance.DroneManager.PlaceKey))
                {
                    GameManager.Instance.DroneManager.CanSpawnDrone = true;
                    PlaceDrone();
                }
                break;
            case DroneState.Flying:
                UpdateFlight();
                break;
            default: throw new ArgumentOutOfRangeException();
        }
    }
    public virtual void OnTargetReached()
    {
        GameManager.Instance.DroneManager.PlayExplosionParticle(transform.position);
        AudioManager.Instance.PlayAtPosition("Explosion", transform.position, 200, 2);
        
        // Checking if collided with tower
        var collidersInSphere = Physics.OverlapSphere(transform.position, DroneSettings.CollisionRadius, GameManager.Instance.EnemyGenerator.TowerLayers);
        if (collidersInSphere.Length > 0)
        {
            foreach (var tower in collidersInSphere)
            {
                GameManager.Instance.EnemyGenerator.ReturnEnemyToPool(tower.gameObject);
            }
        }
        
        GameManager.Instance.DroneManager.ReturnDroneToPool(gameObject, Type);
        // Returning drone to pool
        _droneSounds.Stop();
    }
    public virtual void ResetDrone()
    {
        TargetReached = false;
        StartPosition = Vector3.zero;
        TargetPosition = Vector3.zero;
        CurrentState = DroneState.None;
        
        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;
    }
    
    // --- Execution ---
    public void UseDrone()
    {
        _droneSounds = AudioManager.Instance.PlayAttachedAudio("Drone", transform, 200, 2);
        CurrentState = DroneState.Positioning;
    }
    
    private void UpdateDroneStartPosition()
    {
        var hitWater = GameManager.Instance.DroneManager.GetMousePositionOnSelectableLayers(out var position);
        if (!hitWater) return;
        
        var terrainPosition = GameManager.Instance.TerrainGenerator.transform.position;
        var lookTarget = terrainPosition + ControlSettings.TargetOffset;
        
        // Setting drone position
        var bobAmount = Mathf.Sin(Time.time * DroneSettings.BobSpeed) * DroneSettings.BobAmount;
        
        transform.position = position + (Vector3.up * bobAmount) + (Vector3.up * INITIAL_PLACEMENT_HEIGHT);
        transform.rotation = GetRotation(lookTarget);
    }
    private void UpdateDroneHeight()
    {
        var camera = GameManager.Instance.PlayerCamera;
        var ray = camera.ScreenPointToRay(Input.mousePosition);

        var dragPlane = new Plane(-camera.transform.forward, transform.position);

        if (!dragPlane.Raycast(ray, out var enter)) return;
        var hitPoint = ray.GetPoint(enter);
            
        // Clamp the Y position between our defined minimum and maximum heights
        var clampedY = Mathf.Clamp(hitPoint.y, ControlSettings.MinMaxHeight.x, ControlSettings.MinMaxHeight.y);
            
        transform.position = new Vector3(transform.position.x, clampedY, transform.position.z);
            
        var islandPosition = GameManager.Instance.TerrainGenerator.transform.position;
        transform.rotation = GetRotation(islandPosition);
    }
    private void PlaceDrone()
    {
        // Calculate the exact target the visual line is using
        var expectedTarget = GameManager.Instance.TerrainGenerator.transform.position + ControlSettings.TargetOffset;
        var direction = (expectedTarget - transform.position).normalized;

        // Cast the ray in that specific direction, bypassing the visual RotationOffset
        var ray = new Ray(transform.position, direction);
    
        if (!Physics.Raycast(ray, out var hit)) 
        {
            CurrentState = DroneState.Positioning;
            return;
        }

        CurrentState = DroneState.Flying;
        StartPosition = transform.position;
        TargetPosition = hit.point; 
    }
    private void UpdateFlight()
    {
        // Update drone rotation and position based on flight path
        var t = InverseLerp(StartPosition, TargetPosition, transform.position);
        
        // Calculate the next T step for this frame based on speed
        var distanceTotal = Vector3.Distance(StartPosition, TargetPosition);
        if (distanceTotal == 0) distanceTotal = 0.001f; // Prevent divide by zero
        var currentSpeed = ControlSettings.FlightSpeed.Evaluate(t);
        var nextT = Mathf.Clamp01(t + ((ControlSettings.BaseSpeed * currentSpeed * Time.deltaTime) / distanceTotal));

        // Set rotation first so TransformDirection evaluates using the correct forward orientation
        transform.rotation = GetRotation(TargetPosition);
        transform.position = GetNextFlightPosition(nextT);

        // Checking if target was reached (checking base position to prevent curves missing the trigger)
        var basePosition = Vector3.Lerp(StartPosition, TargetPosition, nextT);
        if (!(Vector3.Distance(basePosition, TargetPosition) < MINIMUM_REACHED_DISTANCE) && nextT < 1f) return;
        TargetReached = true;
        OnTargetReached();
    }

    private void UpdateDroneVisuals()
    {
        if (CurrentState != DroneState.Flying)
        {
            var floorRay = new Ray(transform.position, Vector3.down);
            if (!Physics.Raycast(floorRay, out var hit)) return;
            
            DroneSettings.DroneToFloorLine.gameObject.SetActive(true);
            DroneSettings.DroneToTargetLine.gameObject.SetActive(true);
            DroneSettings.FloorVisual.gameObject.SetActive(true);
            
            DroneSettings.DroneToFloorLine.SetPosition(0, transform.position);
            DroneSettings.DroneToFloorLine.SetPosition(1, hit.point);
            
            var islandPosition = GameManager.Instance.TerrainGenerator.transform.position + ControlSettings.TargetOffset;
            DroneSettings.DroneToTargetLine.SetPosition(0, transform.position);
            DroneSettings.DroneToTargetLine.SetPosition(1, islandPosition);

            DroneSettings.FloorVisual.position = hit.point + DroneSettings.FloorVisualOffset;
            DroneSettings.FloorVisual.rotation = Quaternion.identity;
        }
        else
        {
            DroneSettings.DroneToFloorLine.gameObject.SetActive(false);
            DroneSettings.DroneToTargetLine.gameObject.SetActive(false);
            DroneSettings.FloorVisual.gameObject.SetActive(false);
        }
    }
    
    // --- Utils ---
    private Vector3 GetNextFlightPosition(float t)
    {
        var currentXPosition = ControlSettings.FlightPathX.Evaluate(t);
        var currentYPosition = ControlSettings.FlightPathY.Evaluate(t);

        // Calculate the straight line base position at time t
        var basePosition = Vector3.Lerp(StartPosition, TargetPosition, t);

        // 1. Get the pure direction of the flight path (ignoring visual tilts)
        var flightDirection = (TargetPosition - StartPosition).normalized;
        if (flightDirection == Vector3.zero) flightDirection = Vector3.forward; 
    
        // 2. Create a rotation that looks straight down the path
        var pathRotation = Quaternion.LookRotation(flightDirection);

        // 3. Apply the offsets relative to this pure path rotation
        var offset = pathRotation * new Vector3(
            currentXPosition * (1 - ControlSettings.InlineStrength) * ControlSettings.PathMultiplier, 
            currentYPosition * (1 - ControlSettings.InlineStrength) * ControlSettings.PathMultiplier, 
            0
        );

        return basePosition + offset;
    }
    private Quaternion GetRotation(Vector3 target)
    {
        return Quaternion.Euler(Quaternion.LookRotation(target - transform.position).eulerAngles + DroneSettings.RotationOffset);
    }
    private static float InverseLerp(Vector3 start, Vector3 end, Vector3 current)
    {
        // 1. Get the directional vectors
        var startToEnd = end - start;
        var startToCurrent = current - start;

        // 2. Project the current position onto the start-to-end line
        var t = Vector3.Dot(startToCurrent, startToEnd) / startToEnd.sqrMagnitude;

        // 3. Clamp the value so it never goes below 0 or above 1
        return Mathf.Clamp01(t);
    }

    private AudioSource _droneSounds;
}

[Serializable]
public struct DroneSettings
{
    [Header("Settings")]
    public Vector3 RotationOffset;
    public float BobSpeed;
    public float BobAmount;
    public List<Transform> Wings;
    public float CollisionRadius;
    
    [Header("Visuals")] 
    public LineRenderer DroneToFloorLine;
    public LineRenderer DroneToTargetLine;
    public Transform FloorVisual;
    public Vector3 FloorVisualOffset;
}

[Serializable]
public struct DroneControlSettings
{
    [Header("Path Configuration")]
    public float BaseSpeed;
    [Range(0, 1)] public float InlineStrength;
    public float PathMultiplier;
    public AnimationCurve FlightPathX;
    public AnimationCurve FlightPathY;
    public AnimationCurve FlightSpeed;

    [Header("Placement Settings")] 
    public Vector2 MinMaxHeight;
    public Vector3 TargetOffset;
}

public enum DroneState
{
    None,
    Positioning,
    AdjustingHeight,
    Flying
}

public enum DroneType
{
    Default,
}