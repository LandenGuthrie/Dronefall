using System;
using UnityEngine;

public class BaseEnemy : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform EnemyTower;
    [SerializeField] private Animator EnemyAnimations;

    [Header("Settings")]
    [SerializeField] private BaseEnemySettings Settings;

    public EnemyType Type;
    public EnemyState CurrentState { get; private set; }
    public Transform Tower => EnemyTower;

    public event Action OnIdle;
    public event Action OnWalk;
    public event Action OnConfused;

    //--- Unity
    protected virtual void Start()
    {
        _targetPosition = transform.position;
    }
    protected virtual void Update()
    {
        var walkSpeed = Settings.WalkSpeed * Settings.WalkSpeedCurve.Evaluate(Mathf.Clamp01(Time.time - _startWalkingTime));
        transform.position = Vector3.MoveTowards(transform.position, _targetPosition, walkSpeed * Time.deltaTime);
        if (_targetPosition != transform.position)
        {
            var targetLookRotation = Quaternion.LookRotation(_targetPosition - transform.position);
            var targetRotation = Quaternion.Euler(new Vector3(transform.eulerAngles.x, targetLookRotation.eulerAngles.y, transform.eulerAngles.z));
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Settings.RotationSpeed * Time.deltaTime);
        }
        if (CurrentState == EnemyState.Walking && Vector3.Distance(transform.position, _targetPosition) < 0.01f)
        {
            transform.position = _targetPosition;
            SetEnemyState(EnemyState.Idle);
        }
    }

    //--- StateActions
    public void SetEnemyState(EnemyState state)
    {
        CurrentState = state;
        switch (CurrentState)
        {
            case EnemyState.Idle:
            {
                OnIdleStart();
                break;
            }
            case EnemyState.Walking:
            {
                OnWalkStart();
                break;
            }
            case EnemyState.Confused:
            {
                OnConfusedStart();
                break;
            }
            default:
            {
                throw new ArgumentOutOfRangeException();
            }
        }
    }
    protected virtual void OnIdleStart()
    {
        PlayAnimation(Settings.IdleAnimationName);
        OnIdle?.Invoke();
    }
    protected virtual void OnWalkStart()
    {
        PlayAnimation(Settings.WalkAnimationName);
        OnWalk?.Invoke();
    }
    protected virtual void OnConfusedStart()
    {
        OnConfused?.Invoke();
    }

    //--- Helpers
    public void PlayAnimation(string animationName)
    {
        EnemyAnimations.CrossFade(animationName, Settings.AnimationTransitionDuration);
    }
    public void SetTargetPosition(Vector3 targetPosition)
    {
        if (_targetPosition == targetPosition) return;
        SetEnemyState(EnemyState.Walking);
        _targetPosition = targetPosition;
        _startWalkingTime = Time.time;
    }

    private float _startWalkingTime;
    private Vector3 _targetPosition;
}

[Serializable]
public struct BaseEnemySettings
{
    public AnimationCurve WalkSpeedCurve;
    public float WalkSpeed;
    public float RotationSpeed;
    public float AnimationTransitionDuration;
    public string IdleAnimationName;
    public string WalkAnimationName;
}

public enum EnemyState
{
    Idle,
    Walking,
    Confused,
}

public enum EnemyType
{
    Weak = 0,
}