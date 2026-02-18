using System;
using UnityEngine;

public class BaseEnemy : MonoBehaviour
{
    [Header("Common Enemy Data")]
    public Transform EnemyTower;
    public Animator EnemyAnimations;
    
    [Header("Common Enemy Settings")]
    [SerializeField] private AnimationCurve WalkSpeedCurve;
    [SerializeField] private float WalkSpeed;
    [SerializeField] private float RotationSpeed;

    [Header("Animations")]
    [SerializeField] private float AnimationTransitionDuration = 0.3f;
    [SerializeField] private string IdleAnimationName = "Idle";
    [SerializeField] private string WalkAnimationName = "Walk";
    
    public EnemyState CurrentState { get; private set; }
    public event Action OnIdle;
    public event Action OnWalk;

    public void Start() => OnStart();
    public void Update() => OnUpdate();

    protected virtual void OnStart()
    {
        _targetPosition = transform.position;
    }
    protected virtual void OnUpdate()
    {
        // Updating enemy position based on target position
        var walkSpeed = WalkSpeed * WalkSpeedCurve.Evaluate(Mathf.Clamp01(Time.time - _startWalkingTime));
        transform.position = Vector3.MoveTowards(transform.position, _targetPosition, walkSpeed * Time.deltaTime);
        
        // Updating enemy rotation based on target position
        if (_targetPosition != transform.position)
        {
            var targetLookRotation = Quaternion.LookRotation(_targetPosition - transform.position);
            var targetRotation = Quaternion.Euler(new Vector3(transform.eulerAngles.x, targetLookRotation.eulerAngles.y, transform.eulerAngles.z));
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, RotationSpeed * Time.deltaTime);
        }
        
        // Updating player state
        if (CurrentState == EnemyState.Walking && Vector3.Distance(transform.position, _targetPosition) < 0.01f)
        {
            transform.position = _targetPosition;
            SetEnemyState(EnemyState.Idle);
        }
    }

    public void SetTargetPosition(Vector3 targetPosition)
    {
        if (_targetPosition == targetPosition) return;
        SetEnemyState(EnemyState.Walking);
        _targetPosition = targetPosition;
        _startWalkingTime = Time.time;
    }

    public void SetEnemyState(EnemyState state)
    {
        CurrentState = state;
        switch (CurrentState)
        {
            case EnemyState.Idle:
            {
                EnemyAnimations.CrossFade(IdleAnimationName, AnimationTransitionDuration);
                OnIdle?.Invoke();
                break;
            }
            case EnemyState.Walking:
            {
                EnemyAnimations.CrossFade(WalkAnimationName, AnimationTransitionDuration);
                OnWalk?.Invoke();
                break;
            }

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private float _startWalkingTime;
    private Vector3 _targetPosition;
}

public enum EnemyState
{
    Idle,
    Walking,
}