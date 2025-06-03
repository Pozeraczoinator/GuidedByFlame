using UnityEngine;
using UnityEngine.AI;

public class SkeletonAI : MonoBehaviour
{
    [SerializeField] Transform[] targets;
    [SerializeField] Transform spriteTransform;
    [SerializeField] float stoppingDistance = 0.002f;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private float detectionRange = 5f;
    [SerializeField] private float chaseStopDistance = 1.5f;
    [SerializeField] private float chaseMaxRange = 2f;

    private NavMeshAgent agent;
    private int currentTargetIndex = 0;
    private bool isChasingPlayer = false;
    private bool isPlayerVisible = true;
    private SkeletonAnimation animationHandler;

    public delegate void ChaseStatusChanged(bool isChasing);
    public event ChaseStatusChanged OnChaseStatusChanged;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.updateRotation = false;
        agent.updateUpAxis = false;

        animationHandler = GetComponent<SkeletonAnimation>();

        if (targets.Length > 0)
        {
            agent.SetDestination(targets[currentTargetIndex].position);
        }
    }

    void Update()
    {
        if (targets.Length == 0 || playerTransform == null) return;

        float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);
        bool wasChasing = isChasingPlayer;

        // Warunek rozpoczęcia pościgu
        if (!isChasingPlayer && isPlayerVisible && distanceToPlayer <= detectionRange)
        {
            isChasingPlayer = true;
        }

        // Jeśli gracz w pościgu
        if (isChasingPlayer)
        {
            if (isPlayerVisible && distanceToPlayer <= chaseStopDistance)
            {
                KillPlayer();
                isChasingPlayer = false;
            }
            else if (isPlayerVisible && distanceToPlayer <= chaseMaxRange)
            {
                agent.SetDestination(playerTransform.position);
            }
            else
            {
                // Gracz zniknął lub uciekł poza zasięg
                isChasingPlayer = false;
                GoToNextPatrolPoint();
            }
        }
        else
        {
            if (!agent.pathPending && agent.remainingDistance <= stoppingDistance)
            {
                GoToNextPatrolPoint();
            }
        }

        if (wasChasing != isChasingPlayer)
        {
            OnChaseStatusChanged?.Invoke(isChasingPlayer);
        }

        animationHandler?.UpdateAnimation(agent.velocity);
    }

    private void GoToNextPatrolPoint()
    {
        currentTargetIndex = (currentTargetIndex + 1) % targets.Length;
        agent.SetDestination(targets[currentTargetIndex].position);
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

    void KillPlayer()
    {
        playerTransform.GetComponent<PlayerHealth>()?.Die();
    }

    public void SetPlayerVisible(bool visible)
    {
        isPlayerVisible = visible;
    }
}