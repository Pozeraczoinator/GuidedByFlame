using UnityEngine;
using UnityEngine.AI;
using UnityEngine.U2D.Animation;

public class EnemyAI_Second : MonoBehaviour
{
    [SerializeField] Transform[] targets;
    [SerializeField] Transform spriteTransform;
    [SerializeField] float stoppingDistance = 0.002f;

    private NavMeshAgent agent;
    private int currentTargetIndex = 0;

    // Animacja
[SerializeField] private SpriteResolver spriteResolver;
[SerializeField] private float frameRate = 6f;

private float animationTimer = 0f;
private int currentFrame = 0;
private bool isWalking = false;

private enum Direction { Down, Up, Left, Right }
private Direction currentDirection = Direction.Down;

private string[][] directionLabels = new string[4][] {
    new string[] { "VampireWalk_6", "VampireWalk_7", "VampireWalk_14", "VampireWalk_15" }, // Down
    new string[] { "VampireWalk_2", "VampireWalk_3", "VampireWalk_10", "VampireWalk_11" }, // Up
    new string[] { "VampireWalk_4", "VampireWalk_5", "VampireWalk_12", "VampireWalk_13" }, // Left
    new string[] { "VampireWalk_0", "VampireWalk_1", "VampireWalk_8", "VampireWalk_9" }    // Right
};

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.updateRotation = false;
        agent.updateUpAxis = false;

        if (targets.Length > 0)
        {
            agent.SetDestination(targets[currentTargetIndex].position);
        }

        spriteResolver = GetComponent<SpriteResolver>();
    }

    void Update()
    {
        if (targets.Length == 0) return;

        if (!agent.pathPending && agent.remainingDistance <= stoppingDistance)
        {
            currentTargetIndex = (currentTargetIndex + 1) % targets.Length;
            agent.SetDestination(targets[currentTargetIndex].position);
        }

        Vector3 velocity = agent.velocity;
        isWalking = velocity.magnitude > 0.05f;

        if (isWalking)
        {
            if (Mathf.Abs(velocity.x) > Mathf.Abs(velocity.y))
            {
                currentDirection = velocity.x > 0 ? Direction.Right : Direction.Left;
            }
            else
            {
                currentDirection = velocity.y > 0 ? Direction.Up : Direction.Down;
            }
        }

        AnimateWalk();
    }

    void LateUpdate()
    {
        if (spriteTransform != null)
        {
            Vector3 pos = spriteTransform.position;
            pos.z = 0;
            spriteTransform.position = pos;
        }
    }

    void AnimateWalk()
{
    string[] currentLabels = directionLabels[(int)currentDirection];

    if (!isWalking)
    {
        spriteResolver.SetCategoryAndLabel("Walk", currentLabels[0]);
        return;
    }

    animationTimer += Time.deltaTime;
    if (animationTimer >= 1f / frameRate)
    {
        animationTimer = 0f;
        currentFrame = (currentFrame + 1) % currentLabels.Length;
        spriteResolver.SetCategoryAndLabel("Walk", currentLabels[currentFrame]);
    }
}
}