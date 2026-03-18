using UnityEngine;

public class PlayerIndicator : MonoBehaviour
{
    [SerializeField] private GameObject chaseIndicator; // np. UI z ikonk� lub sprite

    private EnemyAI_Second enemyAI;
    private SkeletonAI skeletonAI;

    private SkullAI skullAI;
    private MusicManager musicManager;

    void Start()
    {
        if (chaseIndicator == null)
        {
            Debug.LogWarning("ChaseIndicator nie przypisany!");
            return;
        }
        musicManager = FindAnyObjectByType<MusicManager>();
        chaseIndicator.SetActive(false);

        // Znajd� wroga - je�li masz wielu, trzeba zmieni� logik�
        enemyAI = FindFirstObjectByType<EnemyAI_Second>();
        if (enemyAI != null)
        {
            enemyAI.OnChaseStatusChanged += HandleChaseStatusChanged;
        }
        else
        {
            Debug.LogWarning("Nie znaleziono EnemyAI_Second");
        }

        skullAI = FindFirstObjectByType<SkullAI>();
        if (skullAI != null)
        {
            skullAI.OnChaseStatusChanged += HandleChaseStatusChanged;
        }
        else
        {
            Debug.LogWarning("Nie znaleziono SkullAI");
        }

        skeletonAI = FindFirstObjectByType<SkeletonAI>();
        if (skeletonAI != null)
        {
            skeletonAI.OnChaseStatusChanged += HandleChaseStatusChanged;
        }
        else
        {
            Debug.LogWarning("Nie znaleziono SkeletonAI");
        }
    }

    void HandleChaseStatusChanged(bool isChasing)
    {
        chaseIndicator.SetActive(isChasing);
        musicManager.StartChase();
    }

    void OnDestroy()
    {
        if (enemyAI != null)
        {
            enemyAI.OnChaseStatusChanged -= HandleChaseStatusChanged;
        }

        if (skullAI != null)
        {
            skullAI.OnChaseStatusChanged -= HandleChaseStatusChanged;
        }

        if (skeletonAI != null)
        {
            skeletonAI.OnChaseStatusChanged -= HandleChaseStatusChanged;
        }
    }
}
