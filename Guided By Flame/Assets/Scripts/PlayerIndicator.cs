using UnityEngine;

public class PlayerIndicator : MonoBehaviour
{
    [SerializeField] private GameObject chaseIndicator; // np. UI z ikonk¹ lub sprite

    private EnemyAI_Second enemyAI;

    void Start()
    {
        if (chaseIndicator == null)
        {
            Debug.LogWarning("ChaseIndicator nie przypisany!");
            return;
        }

        chaseIndicator.SetActive(false);

        // ZnajdŸ wroga - jeœli masz wielu, trzeba zmieniæ logikê
        enemyAI = FindFirstObjectByType<EnemyAI_Second>();
        if (enemyAI != null)
        {
            enemyAI.OnChaseStatusChanged += HandleChaseStatusChanged;
        }
        else
        {
            Debug.LogWarning("Nie znaleziono EnemyAI_Second");
        }
    }

    void HandleChaseStatusChanged(bool isChasing)
    {
        chaseIndicator.SetActive(isChasing);
    }

    void OnDestroy()
    {
        if (enemyAI != null)
        {
            enemyAI.OnChaseStatusChanged -= HandleChaseStatusChanged;
        }
    }
}
