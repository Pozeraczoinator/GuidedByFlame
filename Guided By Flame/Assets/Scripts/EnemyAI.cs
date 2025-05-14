using UnityEngine;
using UnityEngine.U2D.Animation;


[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
public class EnemyAI : MonoBehaviour
{
    private enum AIState { Patrol, Chase }
    private AIState currentState = AIState.Patrol;

    [Header("Patrol Settings")]
    public Transform[] patrolPoints;
    public float patrolSpeed = 2f;
    private int currentPointIndex = 0;
    private int direction = 1;

    private Animator animator;

    [Header("Chase Settings")]
    public float detectionRange = 5f;
    public float chaseSpeed = 3f;

    private Transform player;
    private Rigidbody2D rb;
    private CircleCollider2D cc;
    private int obstacleMask;

    //animacje
[SerializeField] private SpriteResolver spriteResolver;

[Header("Animation Settings")]
[SerializeField] private float frameRate = 6f;

private float animationTimer = 0f;
private int currentFrame = 0;
private bool isWalking = false;

private string[][] directionLabels = new string[4][] {
    new string[] { "VampireWalk_6", "VampireWalk_7", "VampireWalk_14", "VampireWalk_15" }, // Down
    new string[] { "VampireWalk_2", "VampireWalk_3", "VampireWalk_10", "VampireWalk_11" }, // Up
    new string[] { "VampireWalk_4", "VampireWalk_5", "VampireWalk_12", "VampireWalk_13" }, // Left
    new string[] { "VampireWalk_0", "VampireWalk_1", "VampireWalk_8", "VampireWalk_9" }    // Right
};

private enum Direction { Down, Up, Left, Right }
private Direction currentDirection = Direction.Down;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        cc = GetComponent<CircleCollider2D>();
        player = GameObject.FindWithTag("Player")?.transform;
        obstacleMask = LayerMask.GetMask("Obstacle");

        // 1) Dynamic body + Continuous CCD
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        spriteResolver = GetComponent<SpriteResolver>();

    }

    void FixedUpdate()
    {

        if (player == null) return;

        // 2) Wybieramy stan
        float distToPlayer = Vector2.Distance(transform.position, player.position);
        currentState = (distToPlayer <= detectionRange) ? AIState.Chase : AIState.Patrol;

        // 3) Obliczamy kierunek i prędkość
        Vector2 moveDir;
        float speed;
        if (currentState == AIState.Chase)
        {
            moveDir = (player.position - transform.position).normalized;
            speed = chaseSpeed;
        }
        else
        {
            if (patrolPoints.Length == 0)
            {
                rb.MovePosition(rb.position);
                return;
            }

            Vector2 target = patrolPoints[currentPointIndex].position;
            if (Vector2.Distance(transform.position, target) < 0.2f)
            {
                if (currentPointIndex == patrolPoints.Length - 1 || currentPointIndex == 0)
                    direction *= -1;
                currentPointIndex = Mathf.Clamp(currentPointIndex + direction, 0, patrolPoints.Length - 1);
            }

            moveDir = (target - (Vector2)transform.position).normalized;
            speed = patrolSpeed;
        }

        // 4) Sprawdzamy kolizję *przed* ruchem
        float step = speed * Time.fixedDeltaTime;
        float castDistance = step + cc.radius;
        RaycastHit2D hit = Physics2D.CircleCast(transform.position, cc.radius, moveDir, castDistance, obstacleMask);
        if (hit.collider != null)
        {
            // odbijamy wektor
            moveDir = Vector2.Reflect(moveDir, hit.normal).normalized;
        }

        // 5) Wykonujemy ruch przez MovePosition – w ten sposób Unity nigdy nie przepuści Cię przez collider
        rb.MovePosition(rb.position + moveDir * step);

        // 6) Obrót sprite
        FlipSprite(moveDir.x);
        isWalking = moveDir.magnitude > 0.05f;

        if (isWalking)
        {
            if (Mathf.Abs(moveDir.x) > Mathf.Abs(moveDir.y))
            {
                currentDirection = moveDir.x > 0 ? Direction.Right : Direction.Left;
            }
            else
            {
                currentDirection = moveDir.y > 0 ? Direction.Up : Direction.Down;
            }
        }

        AnimateWalk();
    }

    void FlipSprite(float x)
    {
        if (x == 0) return;
        Vector3 s = transform.localScale;
        s.x = Mathf.Sign(x) * Mathf.Abs(s.x);
        transform.localScale = s;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
    }

void AnimateWalk()
{
    string[] currentLabels = directionLabels[(int)currentDirection];

    if (!isWalking)
    {
        spriteResolver.SetCategoryAndLabel("Walk", currentLabels[0]);
        return;
    }

    animationTimer += Time.fixedDeltaTime;
    if (animationTimer >= 1f / frameRate)
    {
        animationTimer = 0f;
        currentFrame = (currentFrame + 1) % currentLabels.Length;
        spriteResolver.SetCategoryAndLabel("Walk", currentLabels[currentFrame]);
    }
}

}