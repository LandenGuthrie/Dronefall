using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class EnemyBase : MonoBehaviour
{
    [Header("References")]
    public Transform EnemyTower;
    public Animator EnemyAnimations;

    [Header("Settings")]
    public EnemyBaseSettings BaseSettings;

    public EnemyType Type;
    public EnemyState CurrentState { get; private set; }
    public GameObject CurrentAttackingDrone { get; private set; }
    
    public event Action OnIdle;
    public event Action OnWalk;
    public event Action OnConfused;

    //--- Unity
    protected virtual void OnDrawGizmos()
    {
        if (!BaseSettings.DrawGizmos) return;
        Gizmos.color = new Color(1, 0, 0, 0.4f);
        Gizmos.DrawSphere(transform.position, BaseSettings.AttackRadius);
    }

    protected virtual void Start()
    {
        _targetPosition = transform.position;
        _cachedDrones = GameManager.Instance.DroneManager.GetActiveDrones();
    }

    protected virtual void Update()
    {
        var activeDrones =  GameManager.Instance.DroneManager.GetActiveDrones();
        _cachedDrones.Clear();
        foreach (var d in activeDrones)
        {
            if (d.TryGetComponent(out DroneBase droneComp))
            {
                if (
                    droneComp.TargetReached
                    || droneComp.CurrentState == DroneState.Positioning
                    || droneComp.CurrentState == DroneState.AdjustingHeight) continue;
                _cachedDrones.Add(d);
            }
        }
        // 1. Validate the current target (if we have one)
        if (CurrentAttackingDrone != null)
        {
            bool isInactive = !CurrentAttackingDrone.activeSelf;
            float distanceToCurrent = Vector3.Distance(transform.position, CurrentAttackingDrone.transform.position);

            // If the drone died/deactivated or walked out of range, drop it
            if (isInactive || distanceToCurrent > BaseSettings.AttackRadius)
            {
                CurrentAttackingDrone = null;
                
                // Revert to walking if we haven't reached our destination, otherwise Idle
                if (Vector3.Distance(transform.position, _targetPosition) > 0.01f)
                    SetEnemyState(EnemyState.Walking);
                else
                    SetEnemyState(EnemyState.Idle);
            }
        }

        // 2. If we don't have a target, look for the closest one in range
        // FIX: Do NOT look for targets if we are currently confused!
        if (CurrentAttackingDrone == null && CurrentState != EnemyState.Confused)
        {
            GameObject closestDrone = null;
            var closestDistance = float.MaxValue;

            foreach (var drone in _cachedDrones)
            {
                if (drone == null || !drone.activeSelf) continue;

                var distance = Vector3.Distance(transform.position, drone.transform.position);
                
                // Only care if it's the closest AND within attack range
                if (distance < closestDistance && distance <= BaseSettings.AttackRadius)
                {
                    closestDistance = distance;
                    closestDrone = drone;
                }
            }

            // 3. If we found a valid drone in range, lock on and attack
            if (closestDrone != null)
            {
                CurrentAttackingDrone = closestDrone;
                SetEnemyState(EnemyState.Attacking); 
            }
        }

        // 4. If we are currently attacking, stop the walking logic
        if (CurrentState == EnemyState.Attacking && CurrentAttackingDrone != null)
        {
            var targetLookRotation = Quaternion.LookRotation(CurrentAttackingDrone.transform.position - transform.position);
            var targetRotation = Quaternion.Euler(new Vector3(transform.eulerAngles.x, targetLookRotation.eulerAngles.y, transform.eulerAngles.z));
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, BaseSettings.RotationSpeed * Time.deltaTime);
            return;
        }
        
        // --- Existing Walking Logic Below ---
        if (CurrentState == EnemyState.Walking)
        {
            var walkSpeed = BaseSettings.WalkSpeed * BaseSettings.WalkSpeedCurve.Evaluate(Mathf.Clamp01(Time.time - _startWalkingTime));
            transform.position = Vector3.MoveTowards(transform.position, _targetPosition, walkSpeed * Time.deltaTime);
            
            if (_targetPosition != transform.position)
            {
                var targetLookRotation = Quaternion.LookRotation(_targetPosition - transform.position);
                var targetRotation = Quaternion.Euler(new Vector3(transform.eulerAngles.x, targetLookRotation.eulerAngles.y, transform.eulerAngles.z));
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, BaseSettings.RotationSpeed * Time.deltaTime);
            }
            
            if (Vector3.Distance(transform.position, _targetPosition) < 0.01f)
            {
                transform.position = _targetPosition;
                SetEnemyState(EnemyState.Idle);
            }
        }
    }
    
    //--- StateActions
    public void SetEnemyState(EnemyState state)
    {
        // Prevent duplicate state initialization (Critical for State Machines!)
        if (CurrentState == state) return; 

        CurrentState = state;
        switch (CurrentState)
        {
            case EnemyState.Idle:
                OnIdleStart();
                break;
            case EnemyState.Walking:
                OnWalkStart();
                break;
            case EnemyState.Confused:
                OnConfusedStart();
                break;
            case EnemyState.Attacking:
                OnAttack();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    protected virtual void OnIdleStart()
    {
        PlayAnimation(BaseSettings.IdleAnimationName);
        OnIdle?.Invoke();
    }
    protected virtual void OnWalkStart()
    {
        PlayAnimation(BaseSettings.WalkAnimationName);
        OnWalk?.Invoke();
    }
    protected virtual void OnConfusedStart()
    {
        OnConfused?.Invoke();
    }
    protected virtual void OnAttack()
    {
        PlayAnimation(BaseSettings.AttackAnimationName);
    }
    
    //--- Helpers
    public void PlayAnimation(string animationName)
    {
        EnemyAnimations.CrossFade(animationName, BaseSettings.AnimationTransitionDuration);
    }

    public void SetTargetPosition(Vector3 targetPosition)
    {
        if (_targetPosition == targetPosition) return;
        SetEnemyState(EnemyState.Walking);
        _targetPosition = targetPosition;
        _startWalkingTime = Time.time;
    }

    public int GetCurrentAnimationFrame()
    {
        const int layerIndex = 0;
        var isInTransition = EnemyAnimations.IsInTransition(layerIndex);

        var stateInfo = isInTransition
            ? EnemyAnimations.GetNextAnimatorStateInfo(layerIndex)
            : EnemyAnimations.GetCurrentAnimatorStateInfo(layerIndex);

        var clipInfos = isInTransition
            ? EnemyAnimations.GetNextAnimatorClipInfo(layerIndex)
            : EnemyAnimations.GetCurrentAnimatorClipInfo(layerIndex);

        if (clipInfos.Length == 0) return 0;

        var currentClip = clipInfos[0].clip;
        var currentLoopTime = stateInfo.normalizedTime % 1.0f;
        var exactFrame = currentLoopTime * currentClip.length * currentClip.frameRate;

        return Mathf.RoundToInt(exactFrame);
    }

    private float _startWalkingTime;
    private Vector3 _targetPosition;
    private List<GameObject> _cachedDrones;
}

[Serializable]
public struct EnemyBaseSettings
{
    [Header("Movement Settings")]
    public AnimationCurve WalkSpeedCurve;
    public float WalkSpeed;
    public float RotationSpeed;
    
    [Header("Animation Settings")]
    public float AnimationTransitionDuration;
    public string IdleAnimationName;
    public string WalkAnimationName;
    public string AttackAnimationName;

    [Header("Attack Settings")] 
    public float AttackRadius;

    [Header("Debug")] 
    public bool DrawGizmos;
}

public enum EnemyState
{
    Idle,
    Walking,
    Confused,
    Attacking,
}

public enum EnemyType
{
    Weak = 0,
}