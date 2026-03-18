using UnityEngine;
using UnityEngine.SceneManagement;

public class DeathScreenManager : MonoBehaviour
{
    public void OnRespawnButton()
    {
        // Przeładuj scenę (respawn gracza)
        Debug.Log("Kliknięto Respawn!");
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void OnReturnToMenu()
    {
        // Załaduj główne menu (upewnij się, że dodałeś scenę do build settings)
        SceneManager.LoadScene("MainMenu"); // <-- zamień na nazwę twojej sceny menu
    }


    public void OnNextLevelButton()
    {
        string currentScene = SceneManager.GetActiveScene().name;

        switch (currentScene)
        {
            case "SampleScene":
                SceneManager.LoadScene("SecondLevel");
                break;

            case "SecondLevel":
                SceneManager.LoadScene("ThirdLevel");
                break;

            case "ThirdLevel":
                Debug.Log("To był ostatni poziom – wracamy do menu.");
                SceneManager.LoadScene("MainMenu");
                break;

            default:
                Debug.LogWarning("Nieznana scena: " + currentScene);
                break;
        }
    }


    public void onNewGameButton()
    {
        SceneManager.LoadScene("SampleScene");
    }


}
