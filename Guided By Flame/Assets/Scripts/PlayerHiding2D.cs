using UnityEngine;

public class PlayerHiding : MonoBehaviour
{
    public float hidingRange = 2f;
    public float hidingDuration = 5f;
    public int maxHides = 3;

    private int remainingHides;

    private SpriteRenderer spriteRenderer;
    private EnemyAI_Second enemyAI;
    private SkeletonAI skeletonAI;
    private playerMovement playerMovement;

    private GameObject[] hidingSpots;
    private Transform nearestHidingSpot;

    private bool isHiding = false;
    private float hidingTimer = 0f;

    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        playerMovement = GetComponent<playerMovement>();
        enemyAI = FindFirstObjectByType<EnemyAI_Second>();
        skeletonAI = FindFirstObjectByType<SkeletonAI>();
        hidingSpots = GameObject.FindGameObjectsWithTag("HidingSpot");

        remainingHides = maxHides;
    }

    void Update()
    {
        FindNearestHidingSpot();

        if (isHiding)
        {
            hidingTimer -= Time.deltaTime;
            if (hidingTimer <= 0f)
                Unhide();
        }

        bool nearSpot = nearestHidingSpot != null &&
                        Vector3.Distance(transform.position, nearestHidingSpot.position) <= hidingRange;

        if (nearSpot && Input.GetKeyDown(KeyCode.E))
        {
            if (!isHiding && remainingHides > 0)
            {
                Hide();
            }
            else if (isHiding)
            {
                Unhide();
            }
        }
    }

    void Hide()
    {
        isHiding = true;
        hidingTimer = hidingDuration;
        remainingHides--;

        spriteRenderer.color = new Color(1, 1, 1, 0.3f);
        enemyAI?.SetPlayerVisible(false);
        skeletonAI?.SetPlayerVisible(false);
        playerMovement?.SetCanMove(false);

        Debug.Log($"Gracz si� ukry�. Pozosta�o ukry�: {remainingHides}");
    }

    void Unhide()
    {
        isHiding = false;

        spriteRenderer.color = new Color(1, 1, 1, 1f);
        enemyAI?.SetPlayerVisible(true);
        skeletonAI?.SetPlayerVisible(true);
        playerMovement?.SetCanMove(true);

        Debug.Log("Gracz wyszed� z kryj�wki.");
    }

    void FindNearestHidingSpot()
    {
        float closestDistance = Mathf.Infinity;
        Transform closestSpot = null;

        foreach (GameObject spot in hidingSpots)
        {
            float distance = Vector3.Distance(transform.position, spot.transform.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestSpot = spot.transform;
            }
        }

        nearestHidingSpot = closestSpot;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, hidingRange);
    }

    // Opcjonalnie: metoda do odnawiania ukry�
    public void RefillHides(int amount)
    {
        remainingHides = Mathf.Min(remainingHides + amount, maxHides);
        Debug.Log($"Ukrycia odnowione. Teraz masz: {remainingHides}");
    }
}
