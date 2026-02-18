using System.Collections;
using UnityEngine;

public class WeakEnemy : BaseEnemy
{
    [SerializeField] private Bounds LocalWalkingBounds;
    [SerializeField] private Vector2 RandomizedMoveTime;

    protected override void OnStart()
    {
        base.OnStart();
        _initYDifference = transform.position.y - EnemyTower.transform.position.y;
        RandomizeMove();
    }

    public void RandomizeMove()
    {
        StartCoroutine(Walk(0));
        OnIdle += () =>
        {
            var randomDuration = Random.Range(RandomizedMoveTime.x, RandomizedMoveTime.y);
            StartCoroutine(Walk(randomDuration));
        };
        return;

        IEnumerator Walk(float waitTime)
        {
            yield return new WaitForSeconds(waitTime);
            
            var randomX = Random.Range(LocalWalkingBounds.min.x, LocalWalkingBounds.max.x) + EnemyTower.transform.position.x;
            var yPosition = EnemyTower.transform.position.y + _initYDifference;
            var randomZ = Random.Range(LocalWalkingBounds.min.z, LocalWalkingBounds.max.z) + EnemyTower.transform.position.z;

            var randomPosition = new Vector3(randomX, yPosition, randomZ);
            SetTargetPosition(randomPosition);
        }
    }


    private float _initYDifference;
}
