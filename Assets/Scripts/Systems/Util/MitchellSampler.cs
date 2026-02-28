using System.Collections.Generic;
using UnityEngine;

public class MitchellsBestCandidateSampler
{
    private readonly Texture2D _mask;
    private readonly Bounds _maskBounds;
    private readonly Bounds _spawnBounds;
    
    private readonly List<Vector2> _acceptedPoints;
    private readonly Dictionary<Vector2Int, List<Vector2>> _spatialGrid;
    private readonly float _cellSize;
    private readonly int _candidatesPerPoint;

    public MitchellsBestCandidateSampler(Bounds spawnBounds, Bounds maskBounds, Texture2D mask, int candidatesPerPoint = 15)
    {
        _spawnBounds = spawnBounds;
        _maskBounds = maskBounds;
        _mask = mask;
        _candidatesPerPoint = candidatesPerPoint;
        
        _acceptedPoints = new List<Vector2>();
        _spatialGrid = new Dictionary<Vector2Int, List<Vector2>>();
        
        // Optimize distance checking by compartmentalizing points into cells
        _cellSize = Mathf.Max(1f, spawnBounds.size.x / 50f); 
    }

    /// <summary>
    /// ONLY call this AFTER your physics raycast successfully confirms the placement.
    /// This ensures failed terrain hits don't repel future objects.
    /// </summary>
    public void RegisterAcceptedPosition(Vector2 position)
    {
        _acceptedPoints.Add(position);
        
        var cell = new Vector2Int(Mathf.FloorToInt(position.x / _cellSize), Mathf.FloorToInt(position.y / _cellSize));
        if (!_spatialGrid.TryGetValue(cell, out var list))
        {
            list = new List<Vector2>();
            _spatialGrid[cell] = list;
        }
        list.Add(position);
    }

    /// <summary>
    /// Generates candidates, verifies the mask, and returns the one furthest from all accepted points.
    /// </summary>
    public bool TryGetNextCandidate(out Vector2 bestCandidate)
    {
        bestCandidate = Vector2.zero;
        float maxDistSq = -1f;
        bool foundAnyValid = false;
        
        int maxAttempts = _candidatesPerPoint * 10; 
        int validCandidatesFound = 0;

        for (int i = 0; i < maxAttempts && validCandidatesFound < _candidatesPerPoint; i++)
        {
            float x = Random.Range(_spawnBounds.min.x, _spawnBounds.max.x);
            float y = Random.Range(_spawnBounds.min.z, _spawnBounds.max.z);
            
            // Strictly enforce the mask first
            if (!IsMaskValid(x, y)) continue;
            
            validCandidatesFound++;
            float minDistSq = GetMinDistSqToAccepted(x, y);

            // We want the point that has the LARGEST distance from existing points
            if (minDistSq > maxDistSq)
            {
                maxDistSq = minDistSq;
                bestCandidate = new Vector2(x, y);
                foundAnyValid = true;
            }
        }
        return foundAnyValid;
    }

    private float GetMinDistSqToAccepted(float x, float y)
    {
        if (_acceptedPoints.Count == 0) return float.MaxValue;

        var cell = new Vector2Int(Mathf.FloorToInt(x / _cellSize), Mathf.FloorToInt(y / _cellSize));
        float minDistSq = float.MaxValue;
        bool checkedGrid = false;

        // Check the local 3x3 grid neighborhood
        for (int cx = -1; cx <= 1; cx++)
        {
            for (int cy = -1; cy <= 1; cy++)
            {
                var neighborCell = new Vector2Int(cell.x + cx, cell.y + cy);
                if (_spatialGrid.TryGetValue(neighborCell, out var pointsInCell))
                {
                    checkedGrid = true;
                    foreach (var p in pointsInCell)
                    {
                        float distSq = (p.x - x) * (p.x - x) + (p.y - y) * (p.y - y);
                        if (distSq < minDistSq) minDistSq = distSq;
                    }
                }
            }
        }

        // Fallback if local grid is empty
        if (!checkedGrid)
        {
            foreach (var p in _acceptedPoints)
            {
                float distSq = (p.x - x) * (p.x - x) + (p.y - y) * (p.y - y);
                if (distSq < minDistSq) minDistSq = distSq;
            }
        }

        return minDistSq;
    }

    private bool IsMaskValid(float worldX, float worldZ)
    {
        if (_mask == null) return true;
        var u = Mathf.Clamp01((worldX - _maskBounds.min.x) / _maskBounds.size.x);
        var v = Mathf.Clamp01((worldZ - _maskBounds.min.z) / _maskBounds.size.z);
        return _mask.GetPixelBilinear(u, v).r > 0.5f;
    }
}