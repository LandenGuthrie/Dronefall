using System;
using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

public class Weak : EnemyBase
{
    [Header("Configuration")]
    [SerializeField] private WeakEnemySettings EnemySettings;

    [Header("References")]
    [SerializeField] private Transform ConfusedIndicator;

    //--- Unity
    private void Awake()
    {
        _initYDifference = transform.position.y - EnemyTower.position.y;
    }

    private void OnEnable()
    {
        var yPosition = EnemyTower.position.y + _initYDifference;
        transform.position = new Vector3(EnemyTower.position.x, yPosition, EnemyTower.position.z); 
        SetTargetPosition(transform.position);
        StartCoroutine(AILoop());
    }

    protected override void Update()
    {
        base.Update();
    
        if (CurrentState == EnemyState.Attacking && CurrentAttackingDrone != null)
        {
            // 1. Check if the Animator has caught up and is evaluating the Attack state
            bool isPlayingAttack = EnemyAnimations.GetCurrentAnimatorStateInfo(0).IsName(BaseSettings.AttackAnimationName);
            bool isTransitioningToAttack = EnemyAnimations.IsInTransition(0) && EnemyAnimations.GetNextAnimatorStateInfo(0).IsName(BaseSettings.AttackAnimationName);

            // 2. If we aren't playing or transitioning to the attack animation yet, wait.
            if (!isPlayingAttack && !isTransitioningToAttack)
            {
                return; 
            }

            var keyframe = GetCurrentAnimationFrame();
        
            // If the animation resets, reset our throw flag
            if (keyframe < EnemySettings.ThrowKeyframe)
            {
                _hasThrownThisCycle = false;
            }
            // Throw exactly once when hitting or passing the keyframe
            else if (keyframe >= EnemySettings.ThrowKeyframe && !_hasThrownThisCycle)
            {
                _hasThrownThisCycle = true;
                ThrowRock();
            }
        }

        if (CurrentState == EnemyState.Confused)
        {
            var confusedIndictorRotation = Quaternion.LookRotation(Camera.main.transform.position - ConfusedIndicator.position, Vector3.up);
            ConfusedIndicator.rotation = Quaternion.Euler(0, confusedIndictorRotation.eulerAngles.y + EnemySettings.ConfusedRotationOffset, 0);
        }
    }
    protected override void OnDrawGizmos()
    {
        base.OnDrawGizmos();
        if (!BaseSettings.DrawGizmos || EnemyTower == null) return;
        var oldMatrix = Gizmos.matrix;
        Gizmos.matrix = EnemyTower.localToWorldMatrix;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(EnemySettings.LocalWalkingBounds.center, EnemySettings.LocalWalkingBounds.size);
        Gizmos.matrix = oldMatrix;
    }

    //--- StateOverrides
    protected override void OnAttack()
    {
        base.OnAttack();
        _hasThrownThisCycle = false; // Ensure we can throw when a new attack starts
    }

    protected override void OnConfusedStart()
    {
        base.OnConfusedStart();
        ConfusedIndicator.gameObject.SetActive(true);
        PlayAnimation(EnemySettings.ConfusedAnimationName);
    }

    protected override void OnIdleStart()
    {
        base.OnIdleStart();
        ConfusedIndicator.gameObject.SetActive(false);
        EnemySettings.Rock.SetActive(false);
    }

    //--- AILogic
    private IEnumerator AILoop()
    {
        while (true)
        {
            // Pause AI logic completely while attacking
            if (CurrentState == EnemyState.Attacking)
            {
                yield return new WaitWhile(() => CurrentState == EnemyState.Attacking);
                continue; 
            }
            
            var randomDuration = Random.Range(EnemySettings.RandomizedMoveTime.x, EnemySettings.RandomizedMoveTime.y);
            
            // Use a timer so we can interrupt the wait if an attack starts
            float timer = 0f;
            while (timer < randomDuration && CurrentState != EnemyState.Attacking)
            {
                timer += Time.deltaTime;
                yield return null;
            }

            // If we broke out of the timer due to an attack, restart the loop
            if (CurrentState == EnemyState.Attacking) continue;

            if (EnemySettings.ConfusedChance / 100f >= Random.value)
            {
                yield return StartCoroutine(ConfusionRoutine());
            }
            else
            {
                Walk();
                // Wait until idle, but break instantly if an attack interrupts
                yield return new WaitUntil(() => CurrentState == EnemyState.Idle || CurrentState == EnemyState.Attacking);
            }
        }
    }

    private IEnumerator ConfusionRoutine()
    {
        SetEnemyState(EnemyState.Confused);
        var confusedDuration = Random.Range(EnemySettings.ConfusedTimeRange.x, EnemySettings.ConfusedTimeRange.y);
        
        // Interruptible wait
        float timer = 0f;
        while (timer < confusedDuration && CurrentState == EnemyState.Confused)
        {
            timer += Time.deltaTime;
            yield return null;
        }

        // Only transition to Idle if we are STILL confused (weren't interrupted)
        if (CurrentState == EnemyState.Confused)
        {
            SetEnemyState(EnemyState.Idle);
        }
    }

    private void Walk()
    {
        var randomLocalX = Random.Range(EnemySettings.LocalWalkingBounds.min.x, EnemySettings.LocalWalkingBounds.max.x);
        var randomLocalZ = Random.Range(EnemySettings.LocalWalkingBounds.min.z, EnemySettings.LocalWalkingBounds.max.z);
        var localPosition = new Vector3(randomLocalX, 0f, randomLocalZ);
        var worldPosition = EnemyTower.TransformPoint(localPosition);
        worldPosition.y = EnemyTower.position.y + _initYDifference;
        SetTargetPosition(worldPosition);
    }

    private void ThrowRock()
    {
        // Target - Source = Correct Direction
        var forwardPredictionPos = CurrentAttackingDrone.transform.position +
                                   (CurrentAttackingDrone.transform.forward * EnemySettings.ForwardPrediction);
        var directionToDrone = (forwardPredictionPos - EnemySettings.Rock.transform.position).normalized; 
        
        // Spawn slightly above the enemy so it doesn't spawn at 0,0,0
        var rock = Instantiate(EnemySettings.Rock, EnemySettings.Rock.transform.position, Quaternion.identity, null);
        rock.GetComponent<TrailRenderer>().enabled = true;

        if (rock.TryGetComponent(out Rigidbody rb))
        {
            rb.isKinematic = false;
            rb.AddForce(directionToDrone * EnemySettings.ThrowForce, ForceMode.Impulse);
        }
        Destroy(rock.gameObject, 10);
    }

    private bool _hasThrownThisCycle;
    private float _initYDifference;
}

[Serializable]
public struct WeakEnemySettings
{
    public Bounds LocalWalkingBounds;
    public Vector2 RandomizedMoveTime;
    public float ConfusedRotationOffset;
    [Range(0, 100)] public float ConfusedChance;
    public Vector2 ConfusedTimeRange;
    public string ConfusedAnimationName;

    [Header("Attacking")] 
    public GameObject Rock;
    public int ThrowKeyframe;
    public float ThrowForce;
    public float ForwardPrediction;
}