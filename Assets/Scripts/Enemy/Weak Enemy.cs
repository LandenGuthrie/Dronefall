using System;
using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

public class WeakEnemy : BaseEnemy
{
    [Header("Configuration")]
    [SerializeField] private WeakEnemySettings EnemySettings;

    [Header("References")]
    [SerializeField] private Transform ConfusedIndicator;

    //--- Unity
    private void Awake()
    {
        _initYDifference = transform.position.y - Tower.position.y;
    }
    private void OnEnable()
    {
        var yPosition = Tower.position.y + _initYDifference;
        transform.position = new Vector3(Tower.position.x, yPosition, Tower.position.z); 
        SetTargetPosition(transform.position);
        StartCoroutine(AILoop());
    }
    protected override void Update()
    {
        base.Update();
        if (CurrentState == EnemyState.Confused)
        {
            var confusedIndictorRotation = Quaternion.LookRotation(Camera.main.transform.position - ConfusedIndicator.position, Vector3.up);
            ConfusedIndicator.rotation = Quaternion.Euler(0, confusedIndictorRotation.eulerAngles.y + EnemySettings.ConfusedRotationOffset, 0);
        }
    }
    private void OnDrawGizmos()
    {
        if (!EnemySettings.ShowGizmos || Tower == null) return;
        var oldMatrix = Gizmos.matrix;
        Gizmos.matrix = Tower.localToWorldMatrix;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(EnemySettings.LocalWalkingBounds.center, EnemySettings.LocalWalkingBounds.size);
        Gizmos.matrix = oldMatrix;
    }

    //--- StateOverrides
    protected override void OnConfusedStart()
    {
        base.OnConfusedStart();
        ConfusedIndicator.gameObject.SetActive(true);
        PlayAnimation(EnemySettings.ConfusedAnimationName);
    }
    protected override void OnIdleStart()
    {
        base.OnIdleStart();
        if (ConfusedIndicator != null)
        {
            ConfusedIndicator.gameObject.SetActive(false);
        }
    }

    //--- AILogic
    private IEnumerator AILoop()
    {
        while (true)
        {
            var randomDuration = Random.Range(EnemySettings.RandomizedMoveTime.x, EnemySettings.RandomizedMoveTime.y);
            yield return new WaitForSeconds(randomDuration);
            if (EnemySettings.ConfusedChance / 100f >= Random.value)
            {
                yield return StartCoroutine(ConfusionRoutine());
            }
            else
            {
                Walk();
                yield return new WaitUntil(() => CurrentState == EnemyState.Idle);
            }
        }
    }
    private IEnumerator ConfusionRoutine()
    {
        SetEnemyState(EnemyState.Confused);
        var confusedDuration = Random.Range(EnemySettings.ConfusedTimeRange.x, EnemySettings.ConfusedTimeRange.y);
        yield return new WaitForSeconds(confusedDuration);
        SetEnemyState(EnemyState.Idle);
    }
    private void Walk()
    {
        var randomLocalX = Random.Range(EnemySettings.LocalWalkingBounds.min.x, EnemySettings.LocalWalkingBounds.max.x);
        var randomLocalZ = Random.Range(EnemySettings.LocalWalkingBounds.min.z, EnemySettings.LocalWalkingBounds.max.z);
        var localPosition = new Vector3(randomLocalX, 0f, randomLocalZ);
        var worldPosition = Tower.TransformPoint(localPosition);
        worldPosition.y = Tower.position.y + _initYDifference;
        SetTargetPosition(worldPosition);
    }

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
    public bool ShowGizmos;
}