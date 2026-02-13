using UnityEngine;

public class SimpleOrbitCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target; // Drag your Player or a central Empty GameObject here

    [Header("Settings")]
    public float targetDistance = 10.0f;
    public float xSpeed = 250.0f;
    public float ySpeed = 120.0f;
    public float zoomSpeed = 5.0f;

    [Header("Limits")]
    public float yMinLimit = -20f; // Prevent going under ground
    public float yMaxLimit = 80f;  // Prevent flipping over top
    public float minDistance = 2.0f;
    public float maxDistance = 50.0f;

    [Header("Smoothing (Higher = Slower)")]
    public float smoothTime = 0.1f; 

    // Internal variables
    private float x = 0.0f;
    private float y = 0.0f;
    private float currentX = 0.0f;
    private float currentY = 0.0f;
    private float currentDistance;
    private float xVelocity = 0.0f;
    private float yVelocity = 0.0f;
    private float zoomVelocity = 0.0f;

    void Start()
    {
        Vector3 angles = transform.eulerAngles;
        x = currentX = angles.y;
        y = currentY = angles.x;
        currentDistance = targetDistance;

        // If no target is assigned, create a temporary one at 0,0,0 so it doesn't crash
        if (target == null)
        {
            GameObject tempTarget = new GameObject("CameraTarget");
            target = tempTarget.transform;
        }
    }

    void LateUpdate()
    {
        if (!target) return;

        // 1. Input (Right Mouse Button to Orbit)
        if (Input.GetMouseButton(1)) 
        {
            x += Input.GetAxis("Mouse X") * xSpeed * 0.02f;
            y -= Input.GetAxis("Mouse Y") * ySpeed * 0.02f;
        }

        // 2. Clamp Rotation
        y = ClampAngle(y, yMinLimit, yMaxLimit);

        // 3. Input (Zoom)
        targetDistance -= Input.GetAxis("Mouse ScrollWheel") * zoomSpeed;
        targetDistance = Mathf.Clamp(targetDistance, minDistance, maxDistance);

        // 4. Smooth Damping (Makes it fluid)
        currentX = Mathf.SmoothDamp(currentX, x, ref xVelocity, smoothTime);
        currentY = Mathf.SmoothDamp(currentY, y, ref yVelocity, smoothTime);
        currentDistance = Mathf.SmoothDamp(currentDistance, targetDistance, ref zoomVelocity, smoothTime);

        // 5. Apply Position & Rotation
        Quaternion rotation = Quaternion.Euler(currentY, currentX, 0);
        Vector3 negDistance = new Vector3(0.0f, 0.0f, -currentDistance);
        
        Vector3 position = rotation * negDistance + target.position;

        transform.rotation = rotation;
        transform.position = position;
    }

    public static float ClampAngle(float angle, float min, float max)
    {
        if (angle < -360F) angle += 360F;
        if (angle > 360F) angle -= 360F;
        return Mathf.Clamp(angle, min, max);
    }
}