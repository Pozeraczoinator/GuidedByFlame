using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    [SerializeField] private GameObject deathScreen; // <- przypisz w Inspectorze
    [SerializeField] private GameObject winScreen; // <- przypisz w Inspectorze

    public void Die()
    {
        if (deathScreen != null)
            deathScreen.SetActive(true);

        // Zablokuj gracza
        gameObject.SetActive(false);
    }


    public void Win()
    {
        if (winScreen != null)
            winScreen.SetActive(true);

        // Zablokuj gracza
        gameObject.SetActive(false);
    }
}