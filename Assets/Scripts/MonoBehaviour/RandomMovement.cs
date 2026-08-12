using UnityEngine;

public class RandomMovement : MonoBehaviour
{
    public float moveSpeed = 3f;
    private Vector2 targetPosition;
    private Camera mainCamera;
    private float minX, maxX, minY, maxY;

    void Start()
    {
        mainCamera = Camera.main;
        CalculateScreenBounds();
        SetNewRandomPosition();
    }

    void Update()
    {
        // Move toward the random target
        transform.position = Vector2.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);

        // Pick a new target if close to the current one
        if (Vector2.Distance(transform.position, targetPosition) < 0.1f)
        {
            SetNewRandomPosition();
        }
    }

    void CalculateScreenBounds()
    {
        // Get camera bounds in world units
        float camDist = Mathf.Abs(mainCamera.transform.position.z);
        Vector3 lowerLeft = mainCamera.ViewportToWorldPoint(new Vector3(0, 0, camDist));
        Vector3 upperRight = mainCamera.ViewportToWorldPoint(new Vector3(1, 1, camDist));

        // Add a small offset so the sprite stays fully visible
        Renderer spriteRenderer = GetComponent<Renderer>();
        float paddingX = spriteRenderer != null ? spriteRenderer.bounds.extents.x : 0.5f;
        float paddingY = spriteRenderer != null ? spriteRenderer.bounds.extents.y : 0.5f;

        minX = lowerLeft.x + paddingX;
        maxX = upperRight.x - paddingX;
        minY = lowerLeft.y + paddingY;
        maxY = upperRight.y - paddingY;
    }

    void SetNewRandomPosition()
    {
        float randomX = Random.Range(minX, maxX);
        float randomY = Random.Range(minY, maxY);
        targetPosition = new Vector2(randomX, randomY);
    }
}
