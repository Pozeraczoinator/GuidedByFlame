using UnityEngine;

public class EnemyAI : MonoBehaviour
{
    [Header("Patrol Settings")]
    public Transform[] patrolPoints; // Punkty A, B, C, D (dodaj w Inspectorze)
    public float patrolSpeed = 2f;
    private int currentPointIndex = 0;
    private int direction = 1; // 1 = do przodu, -1 = do tyłu

    [Header("Chase Settings")]
    public float detectionRange = 5f;
    public float chaseSpeed = 3f;

    private Transform player;
    private Rigidbody2D rb;

    void Start()
    {
        player = GameObject.FindGameObjectWithTag("Player")?.transform;
        rb = GetComponent<Rigidbody2D>();
    }

    void FixedUpdate()
    {
        if (player == null) return;

        float distanceToPlayer = Vector2.Distance(transform.position, player.position);

        if (distanceToPlayer <= detectionRange)
        {
            ChasePlayer();
        }
        else
        {
            Patrol();
        }
    }

    void Patrol()
    {
        if (patrolPoints.Length == 0) return;

        Transform targetPoint = patrolPoints[currentPointIndex];
        Vector2 direction = (targetPoint.position - transform.position).normalized;
        rb.MovePosition(rb.position + direction * patrolSpeed * Time.fixedDeltaTime);

        // Zmiana punktu, gdy jesteśmy wystarczająco blisko
        if (Vector2.Distance(transform.position, targetPoint.position) < 0.2f)
        {
            currentPointIndex = (currentPointIndex + 1) % patrolPoints.Length;
        }
    }

    void ChasePlayer()
    {
        Vector2 direction = (player.position - transform.position).normalized;
        rb.MovePosition(rb.position + direction * chaseSpeed * Time.fixedDeltaTime);
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Obstacle"))
        {
            // Zmiana kierunku na przeciwny
            direction = -1;

            // Można też ustawić pozycję przeciwnika lub zaktualizować stan Rigidbody2D
            // np. ustawienie prędkości w przeciwnym kierunku:
            Rigidbody2D rb = GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.linearVelocity = new Vector2(-rb.linearVelocity.x, rb.linearVelocity.y);  // Zmiana kierunku ruchu
            }

            // Aktualizacja punktu patrolu
            currentPointIndex += direction * 2;
            currentPointIndex = Mathf.Clamp(currentPointIndex, 0, patrolPoints.Length - 1);
        }
    }
}
