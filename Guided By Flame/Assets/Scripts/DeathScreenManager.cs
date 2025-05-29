using UnityEngine;
using UnityEngine.SceneManagement;

public class DeathScreenManager : MonoBehaviour
{
    public void OnRespawnButton()
    {
        // Prze³aduj scenê (respawn gracza)
        Debug.Log("Klikniêto Respawn!");
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void OnReturnToMenu()
    {
        // Za³aduj g³ówne menu (upewnij siê, ¿e doda³eœ scenê do build settings)
        SceneManager.LoadScene("MainMenu"); // <-- zamieñ na nazwê twojej sceny menu
    }
}
