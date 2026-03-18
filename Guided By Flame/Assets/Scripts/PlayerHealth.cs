using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    [SerializeField] private GameObject deathScreen; // <- przypisz w Inspectorze
    [SerializeField] private GameObject winScreen; // <- przypisz w Inspectorze

    private EnemyAI_Second enemyAI;
    private SkeletonAI skeletonAI;

    void Start()
    {
        
        enemyAI = FindFirstObjectByType<EnemyAI_Second>();
        skeletonAI = FindFirstObjectByType<SkeletonAI>();
        
    }

    public void Die()
    {
        if (deathScreen != null)
            deathScreen.SetActive(true);

        KeyPickup.hasKey = false;

        // Zablokuj gracza
        gameObject.SetActive(false);
    }


    public void Win()
    {
        if (winScreen != null)
            winScreen.SetActive(true);

        enemyAI?.SetPlayerVisible(false);
        skeletonAI?.SetPlayerVisible(false);
        KeyPickup.hasKey = false;

        // Zablokuj gracza
        gameObject.SetActive(false);
    }
}